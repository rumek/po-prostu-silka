using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Training;

namespace po_prostu_silka.Application.Training;

/// <summary>
/// Edits a plan and its items.
/// </summary>
public static class UpdateTrainingPlan
{
    /// <summary>
    /// Edits a plan in place: its name and its entire item list.
    ///
    /// <para>
    /// NO RETRY LOOP, and that is not an oversight. This path changes one plan's own rows and moves
    /// nothing across the one-active-plan boundary, so there is no read-then-write sequence to make
    /// atomic. The stamp is still rotated and the outcome still checked, so two trainers editing the
    /// same plan get a clean 409 instead of one silently overwriting the other.
    /// </para>
    /// </summary>
    public static async Task<IResult> HandleAsync(
        Guid id,
        TrainingPlanRequest request,
        ITrainingPlanQuery query,
        ITrainingPlanStore store,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var invalid = TrainingPlanValidator.ValidateShape(request);
        if (invalid is not null)
        {
            return invalid;
        }

        var entity = await store.FindAsync(id, cancellationToken);
        if (entity is null)
        {
            return Results.NotFound();
        }

        // An archived plan is not editable. It is not addressable by any screen either, so reaching
        // here means a stale tab or a hand-made request; 404 rather than 409 because from the
        // client's point of view the thing it is editing no longer exists.
        if (entity.Status != TrainingPlanStatus.Active)
        {
            return Results.NotFound();
        }

        if (entity.MemberId != request.MemberId)
        {
            return TrainingPlanValidator.Refuse("member_changed", 409);
        }

        var exercisesRefused = await TrainingPlanValidator.ValidateExercisesAsync(request, store, cancellationToken);
        if (exercisesRefused is not null)
        {
            return exercisesRefused;
        }

        entity.Name = request.Name.Trim();

        // REPLACED, NOT RECONCILED. Deleting every row and inserting the request's is what keeps
        // Position dense without matching rows by identity, and it is why TrainingPlanItems carries
        // no unique index on (plan, position) that a partial reorder would trip over mid-statement.
        //
        // It goes through the store rather than through entity.Items for a reason found the hard
        // way: assigning a fresh List to a TRACKED parent's collection navigation makes EF resolve
        // the new children against the entries it already holds, and it emitted an UPDATE against a
        // row the same SaveChanges had just deleted - "expected to affect 1 row(s), but actually
        // affected 0". Addressing the rows explicitly leaves nothing for collection fixup to guess.
        store.ReplaceItems(entity, TrainingPlanItemBuilder.BuildItems(request));

        entity.ConcurrencyStamp = Guid.NewGuid().ToString();

        var outcome = await unitOfWork.TrySaveAsync(cancellationToken);
        if (outcome != SaveOutcome.Saved)
        {
            return TrainingPlanValidator.Refuse("conflict", 409);
        }

        return Results.Ok(await query.FindDetailAsync(entity.Id, cancellationToken));
    }
}
