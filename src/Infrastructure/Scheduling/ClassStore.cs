using Microsoft.EntityFrameworkCore;
using po_prostu_silka.Application.Scheduling;
using po_prostu_silka.Domain.Scheduling;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Infrastructure.Scheduling;

/// <summary>
/// Infrastructure side of <see cref="IClassStore"/>. Nothing here saves — the endpoint commits
/// through IUnitOfWork, which is what lets a whole duplicate batch land in one transaction.
/// </summary>
public class ClassStore(AppDbContext db) : IClassStore
{
    public async Task<Class?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        // Tracked, not AsNoTracking: UpdateAsync mutates what this returns and expects the change
        // tracker to notice.
        await db.Classes.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public void Add(Class entity) => db.Classes.Add(entity);

    public void Remove(Class entity) => db.Classes.Remove(entity);

    /// <summary>
    /// Two classes conflict when they share a room, both are Scheduled, and their
    /// [StartsAt, StartsAt + DurationMinutes) intervals intersect.
    ///
    /// <para>
    /// KNOWN LIMITATION - this is a read-then-write race. The caller checks here and writes after,
    /// so two admins creating overlapping classes at the same instant can both pass. No unique index
    /// can express interval overlap, and closing it properly would need serializable isolation,
    /// which EnableRetryOnFailure makes awkward (an explicit transaction must go through
    /// Database.CreateExecutionStrategy().ExecuteAsync or it throws at runtime). Accepted because
    /// exactly one admin account is ever seeded (AdminSeeder), so concurrent admin writes are not a
    /// real scenario for this club. Revisit if a second admin is ever added.
    /// </para>
    /// </summary>
    public async Task<bool> HasRoomConflictAsync(
        string room,
        DateTimeOffset startsAt,
        int durationMinutes,
        Guid? excludingId,
        CancellationToken cancellationToken)
    {
        var endsAt = startsAt.AddMinutes(durationMinutes);

        // Half-open intervals: a class ending exactly when the next begins does NOT conflict, which
        // is what back-to-back classes in one room are.
        //
        // AddMinutes on a column translates to DATEADD, so this whole predicate runs in SQL against
        // the (Room, StartsAt) index rather than pulling the room's classes into memory.
        var conflictsInDatabase = await db.Classes
            .AsNoTracking()
            .AnyAsync(
                c => c.Room == room
                     && c.Status == ClassStatus.Scheduled
                     && (excludingId == null || c.Id != excludingId)
                     && c.StartsAt < endsAt
                     && startsAt < c.StartsAt.AddMinutes(c.DurationMinutes),
                cancellationToken);

        if (conflictsInDatabase)
        {
            return true;
        }

        // ALSO check what is queued but not yet saved. An EF query reads the database, not the
        // change tracker, so without this a batch of Added-but-unsaved copies would be invisible to
        // each other and two of them could be written into the same room and time.
        //
        // Today's only batch is a weekly duplicate, whose copies are seven days apart and so cannot
        // collide with one another - but a checker that silently ignores pending writes is a trap
        // for the next caller, not a saving.
        return db.Classes.Local.Any(
            c => c.Room == room
                 && c.Status == ClassStatus.Scheduled
                 && (excludingId == null || c.Id != excludingId)
                 && db.Entry(c).State == EntityState.Added
                 && c.StartsAt < endsAt
                 && startsAt < c.StartsAt.AddMinutes(c.DurationMinutes));
    }
}
