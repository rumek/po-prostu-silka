using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Paging;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Training;

namespace po_prostu_silka.Application.Training;

/// <summary>
/// Narrow read seam over training plans, so Application does not reference EF Core (AGENTS.md
/// layering). Implemented in Infrastructure.
/// </summary>
public interface ITrainingPlanQuery
{
    /// <summary>One plan with its items in position order, or null. Active or archived.</summary>
    Task<TrainingPlanDetail?> FindDetailAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The member's active plan with its items, or null when they have none.</summary>
    Task<TrainingPlanDetail?> FindActiveForMemberAsync(Guid memberId, CancellationToken cancellationToken);

    /// <summary>
    /// One page of the trainer's member list (S-22): the members a plan may be given to, narrowed by
    /// a NAME-ONLY search, each with their active plan's name. The eligibility rule is the same one
    /// <see cref="IsAssignableAsync"/> answers, so a member drops off this list exactly when a plan
    /// could no longer be created for them.
    /// </summary>
    Task<PagedResult<TrainerMemberSummary>> GetTrainerMembersAsync(
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>
    /// The trainer-safe identity of one member, whatever their status, or null when no such member
    /// exists. Status is not filtered because an admin must be able to reach a blocked member's plan.
    /// </summary>
    Task<AssignableMember?> FindMemberAsync(Guid memberId, CancellationToken cancellationToken);

    /// <summary>
    /// Whether this member may be assigned a plan, and if not, why — so the caller can tell "no such
    /// member", "not active" and "staff" apart without a second round trip.
    ///
    /// <para>
    /// ONE QUESTION RATHER THAN A STATUS, because since S-14 the answer depends on two of them (and
    /// since S-25 on the roles too) and the read side and the write side must not be able to disagree:
    /// <see cref="GetTrainerMembersAsync"/> lists exactly the members this answers
    /// <see cref="MemberAssignability.Assignable"/> for.
    /// </para>
    /// </summary>
    Task<MemberAssignability> IsAssignableAsync(Guid memberId, CancellationToken cancellationToken);

    /// <summary>
    /// One exercise, but ONLY if it appears in the given member's active plan. Null otherwise -
    /// including when the exercise exists and the member simply has not been prescribed it.
    ///
    /// <para>
    /// This is how prd.md:163's "no standalone exercise library browsing" is ENFORCED rather than
    /// merely respected. The scoping is a join against the member's own plan, not a role check, so
    /// there is no way to widen it by holding a different role.
    /// </para>
    /// </summary>
    Task<ExerciseSummary?> FindPlanExerciseAsync(
        Guid memberId,
        Guid exerciseId,
        CancellationToken cancellationToken);
}
