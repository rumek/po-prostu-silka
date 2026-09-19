using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Training;

namespace po_prostu_silka.Application.Training;

/// <summary>
/// The write counterpart. Intention-revealing methods rather than a generic repository — this
/// codebase has no repository pattern and this slice does not introduce one.
///
/// No Remove. Deactivation is a field on the entity, not an operation on the store.
///
/// Nothing here saves. The endpoint commits through <see cref="IUnitOfWork"/>.
/// </summary>
public interface IExerciseStore
{
    Task<Exercise?> FindAsync(Guid id, CancellationToken cancellationToken);

    void Add(Exercise entity);

    /// <summary>
    /// Whether another ACTIVE exercise already holds <paramref name="name"/>. Inactive rows are
    /// invisible here, which is what lets a retired name be reused.
    /// </summary>
    /// <param name="excludingId">
    /// The exercise being edited or activated, so it does not collide with itself. Null when creating.
    /// </param>
    Task<bool> IsNameTakenAsync(string name, Guid? excludingId, CancellationToken cancellationToken);
}
