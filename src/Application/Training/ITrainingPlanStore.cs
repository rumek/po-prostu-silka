using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Training;

namespace po_prostu_silka.Application.Training;

/// <summary>
/// The write counterpart. Intention-revealing methods rather than a generic repository.
///
/// No Remove: a plan is archived, never deleted. <see cref="ReplaceItems"/> is not an exception - it
/// swaps a plan's item rows for a new set, which is replacement within one aggregate rather than
/// deletion of one.
///
/// Nothing here saves. The endpoint commits through <see cref="IUnitOfWork"/>.
/// </summary>
public interface ITrainingPlanStore
{
    /// <summary>One plan, TRACKED with its items - callers mutate what comes back.</summary>
    Task<TrainingPlan?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// The member's active plan, TRACKED, without its items. The assignment path only ever flips this
    /// row's status and rotates its stamp, and loading a plan's items to archive it would be reading
    /// rows to ignore them.
    /// </summary>
    Task<TrainingPlan?> FindActiveForMemberAsync(Guid memberId, CancellationToken cancellationToken);

    void Add(TrainingPlan entity);

    /// <summary>
    /// Swaps the plan's item rows for <paramref name="items"/>, numbered as they arrive.
    ///
    /// <para>
    /// One method rather than a clear and an add, because the two are only ever correct together and
    /// splitting them invites the caller to reach for the collection navigation in between - which is
    /// exactly the mistake this signature exists to prevent (see UpdateAsync).
    /// </para>
    /// </summary>
    void ReplaceItems(TrainingPlan entity, IReadOnlyList<TrainingPlanItem> items);

    /// <summary>
    /// Which of the given exercise ids exist, and whether each is active. Ids that do not exist are
    /// ABSENT from the result rather than present as false - the caller has to tell "no such
    /// exercise" from "retired exercise", because they are different refusals.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, bool>> FindExerciseStatesAsync(
        IReadOnlyCollection<Guid> exerciseIds,
        CancellationToken cancellationToken);
}
