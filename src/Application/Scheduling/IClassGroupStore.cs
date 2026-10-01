using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// The write counterpart. Intention-revealing methods rather than a generic repository — this
/// codebase has no repository pattern and this slice does not introduce one.
///
/// No Remove. FR-006 rules out hard deletion; deactivation is a field on the entity, not an
/// operation on the store.
///
/// Nothing here saves. The endpoint commits through <see cref="IUnitOfWork"/>.
/// </summary>
public interface IClassGroupStore
{
    Task<ClassGroup?> FindAsync(Guid id, CancellationToken cancellationToken);

    void Add(ClassGroup entity);

    /// <summary>
    /// Whether another ACTIVE group already holds <paramref name="name"/>. Inactive groups are
    /// invisible here, which is what lets a retired name be reused (FR-006).
    /// </summary>
    /// <param name="excludingId">
    /// The group being edited or activated, so it does not collide with itself. Null when creating.
    /// </param>
    Task<bool> IsNameTakenAsync(string name, Guid? excludingId, CancellationToken cancellationToken);
}
