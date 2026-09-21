using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Training;

namespace po_prostu_silka.Application.Training;

/// <summary>
/// Builds and assigns a plan (FR-015, FR-016).
/// </summary>
public static class CreateTrainingPlan
{
    /// <summary>
    /// Assigns a new plan, archiving whatever the member was following (prd.md FR-016).
    ///
    /// <para>
    /// THE ONE-ACTIVE-PLAN GUARANTEE IS ENFORCED AGAINST THIS METHOD, not by it. Archiving and
    /// inserting is a read-then-write sequence committed in a single SaveChangesAsync - no explicit
    /// transaction is opened anywhere in this codebase, and opening one here would need the
    /// execution strategy because EnableRetryOnFailure is on. What keeps concurrent assignments
    /// honest is IX_TrainingPlans_MemberId_Active, the filtered unique index every attempt's INSERT
    /// has to get past; the loop below turns its rejection into a retry rather than a 500. The
    /// stamp rotation is the cheaper, earlier half of the same defence - see
    /// TrainingPlan.ConcurrencyStamp for why it is second and not first.
    /// </para>
    ///
    /// <para>
    /// DROPPED AN INJECTED IMemberStore IN S-18. It was bound on every request and never read: the
    /// author is resolved from the principal's member claim, and the assignee is validated through
    /// the query port below.
    /// </para>
    /// </summary>
    public static async Task<IResult> HandleAsync(
        TrainingPlanRequest request,
        ClaimsPrincipal principal,
        ITrainingPlanQuery query,
        ITrainingPlanStore store,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        // THE MEMBER ID IS THE ONLY ONE THIS HANDLER NEEDS. It also resolved the author's ACCOUNT id
        // until S-14 Phase 9 dropped TrainingPlans.AssignedByUserId - which left a UserManager round
        // trip per plan creation feeding a value nothing stored. The claim is enough, and it is free.
        var authorMemberId = principal.GetMemberId();
        if (authorMemberId is null)
        {
            return Results.Unauthorized();
        }

        var invalid = TrainingPlanValidator.ValidateShape(request);
        if (invalid is not null)
        {
            return invalid;
        }

        var memberId = request.MemberId;

        var memberRefused = await TrainingPlanValidator.ValidateMemberAsync(memberId, query, cancellationToken);
        if (memberRefused is not null)
        {
            return memberRefused;
        }

        var exercisesRefused = await TrainingPlanValidator.ValidateExercisesAsync(request, store, cancellationToken);
        if (exercisesRefused is not null)
        {
            return exercisesRefused;
        }

        for (var attempt = 1; attempt <= TrainingPlanItemBuilder.MaxAttempts; attempt++)
        {
            var now = timeProvider.GetUtcNow();

            var current = await store.FindActiveForMemberAsync(memberId, cancellationToken);
            if (current is not null)
            {
                current.Status = TrainingPlanStatus.Archived;
                current.ArchivedAt = now;

                // Rotated so the UPDATE that archives cannot carry a token EF already considers
                // current: a racer who lost the read then fails here instead of travelling all the
                // way down to the index. Removing this line does NOT break the race test - the
                // index still catches it - but it does leave the edit path unguarded.
                current.ConcurrencyStamp = Guid.NewGuid().ToString();
            }

            var created = new TrainingPlan
            {
                Id = Guid.NewGuid(),
                Name = request.Name.Trim(),
                MemberId = memberId,
                AssignedByMemberId = authorMemberId.Value,

                Status = TrainingPlanStatus.Active,
                CreatedAt = now,
                Items = TrainingPlanItemBuilder.BuildItems(request),
            };

            store.Add(created);

            var outcome = await unitOfWork.TrySaveAsync(cancellationToken);
            if (outcome == SaveOutcome.Saved)
            {
                return Results.Ok(await query.FindDetailAsync(created.Id, cancellationToken));
            }

            // UniqueViolation: IX_TrainingPlans_MemberId_Active caught two racers whose INSERTs both
            // claimed the member's active slot. This is the usual outcome, and the reason the
            // invariant holds at all.
            // ConcurrencyConflict: the loser noticed earlier, on the UPDATE that archives the plan
            // another assignment had already archived.
            //
            // Both mean nothing was written. The tracked graph still holds the rejected insert and a
            // plan whose stamp is stale, so it has to go before the next read returns fresh state.
            unitOfWork.DiscardChanges();
        }

        return TrainingPlanValidator.Refuse("conflict", 409);
    }
}
