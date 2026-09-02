using Microsoft.EntityFrameworkCore;
using po_prostu_silka.Application.Scheduling;
using po_prostu_silka.Domain.Scheduling;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Infrastructure.Scheduling;

/// <summary>
/// Infrastructure side of <see cref="IClassTypeStore"/>. Nothing here saves — the endpoint commits
/// through IUnitOfWork.
/// </summary>
public class ClassTypeStore(AppDbContext db) : IClassTypeStore
{
    public async Task<ClassType?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        // Tracked, not AsNoTracking: the update, deactivate and activate handlers all mutate what
        // this returns and expect the change tracker to notice.
        await db.ClassTypes.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public void Add(ClassType entity) => db.ClassTypes.Add(entity);

    /// <summary>
    /// Whether another ACTIVE type holds this name.
    ///
    /// <para>
    /// A plain <c>==</c>, deliberately. SQL Server's default collation is case-insensitive, so this
    /// already refuses "joga" against an active "Joga" — verified against the local engine. Calling
    /// ToLower() would express the same intent while making the predicate non-sargable, so it could
    /// no longer use IX_ClassTypes_Name_Active.
    /// </para>
    ///
    /// <para>
    /// This is a read-then-write race, like ClassStore.HasRoomConflictAsync — the caller checks here
    /// and writes after. Unlike the overlap rule, though, this one has a real backstop: the filtered
    /// unique index means a loser of the race gets a DbUpdateException rather than a second active
    /// type sharing a name. The check exists so the ordinary case returns a clean 409 instead.
    /// </para>
    /// </summary>
    public Task<bool> IsNameTakenAsync(
        string name,
        Guid? excludingId,
        CancellationToken cancellationToken) =>
        db.ClassTypes
            .AsNoTracking()
            .AnyAsync(
                t => t.IsActive
                     && t.Name == name
                     && (excludingId == null || t.Id != excludingId),
                cancellationToken);
}
