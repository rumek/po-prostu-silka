using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Members;
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
    /// <summary>Every active plan, by member display name. Unbounded - a single gym's list.</summary>
    Task<IReadOnlyList<TrainingPlanSummary>> GetActiveAsync(CancellationToken cancellationToken);

    /// <summary>Approved accounts as picker rows, by display name.</summary>
    Task<IReadOnlyList<AssignableMember>> GetAssignableMembersAsync(CancellationToken cancellationToken);

    /// <summary>One plan with its items in position order, or null. Active or archived.</summary>
    Task<TrainingPlanDetail?> FindDetailAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The member's active plan with its items, or null when they have none.</summary>
    Task<TrainingPlanDetail?> FindActiveForMemberAsync(Guid memberId, CancellationToken cancellationToken);

    /// <summary>
    /// The account's status, or null when no such account exists. A status rather than the row: the
    /// caller only needs to know whether a plan may be assigned, and returning ApplicationUser here
    /// would invite a write path to mutate an account through a read seam.
    /// </summary>
    /// <summary>
    /// Whether this member may be assigned a plan, or null when no such member exists — so the caller
    /// can tell "no such member" from "not eligible" without a second round trip.
    ///
    /// <para>
    /// ONE QUESTION RATHER THAN A STATUS, because since S-14 the answer depends on two of them and the
    /// read side and the write side must not be able to disagree: <see cref="GetAssignableMembersAsync"/>
    /// offers exactly the members this returns true for.
    /// </para>
    /// </summary>
    Task<bool?> IsAssignableAsync(Guid memberId, CancellationToken cancellationToken);

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
