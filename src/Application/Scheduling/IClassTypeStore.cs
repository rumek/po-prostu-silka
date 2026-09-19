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
public interface IClassTypeStore
{
    Task<ClassType?> FindAsync(Guid id, CancellationToken cancellationToken);

    void Add(ClassType entity);

    /// <summary>
    /// Whether another ACTIVE type already holds <paramref name="name"/>. Inactive types are
    /// invisible here, which is what lets a retired name be reused (FR-006).
    /// </summary>
    /// <param name="excludingId">
    /// The type being edited or activated, so it does not collide with itself. Null when creating.
    /// </param>
    Task<bool> IsNameTakenAsync(string name, Guid? excludingId, CancellationToken cancellationToken);
}
