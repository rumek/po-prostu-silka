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
        //
        // Both navigations are included for GetByIdAsync, which is the one caller that projects
        // straight from what this returns: an occurrence carries neither its name nor its instructor's
        // display name (prd-v2 FR-007, FR-009, FR-010), so without these that DTO would dereference
        // null.
        //
        // The WRITE paths do not rely on them. They pass the ClassType and ApplicationUser their own
        // validation already resolved, because after an instructor is reassigned the tracked entity's
        // Instructor navigation still points at the PREVIOUS account - projecting from it would give
        // a correct id beside a stale name.
        //
        // Include, not a projection, precisely because the result is tracked and mutable. That is the
        // one place in this codebase where Include is the right tool; the read queries next door stay
        // projections.
        await db.Classes
            .Include(c => c.ClassType)
            .Include(c => c.Instructor)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public void Add(Class entity) => db.Classes.Add(entity);

    public void Remove(Class entity) => db.Classes.Remove(entity);

    /// <summary>
    /// Two classes conflict when both are Scheduled and their
    /// [StartsAt, StartsAt + DurationMinutes) intervals intersect — anywhere in the club.
    ///
    /// <para>
    /// THE ROOM PREDICATE IS GONE, THE RULE IS NOT (prd-v2 FR-012). This was a room-scoped check
    /// until S-06; the club has one room, so scoping by it never excluded anything, and dropping the
    /// field made the real invariant explicit: one club, one class at a time.
    /// </para>
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
    public async Task<bool> HasTimeConflictAsync(
        DateTimeOffset startsAt,
        int durationMinutes,
        Guid? excludingId,
        CancellationToken cancellationToken)
    {
        var endsAt = startsAt.AddMinutes(durationMinutes);

        // Half-open intervals: a class ending exactly when the next begins does NOT conflict, which
        // is what back-to-back classes are.
        //
        // AddMinutes on a column translates to DATEADD, so this whole predicate runs in SQL against
        // IX_Classes_StartsAt rather than pulling classes into memory.
        var conflictsInDatabase = await db.Classes
            .AsNoTracking()
            .AnyAsync(
                c => c.Status == ClassStatus.Scheduled
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
        // each other and two of them could be written into the same time.
        //
        // This half matters MORE since the room predicate went. Today's only batch is a weekly
        // duplicate, whose copies are seven days apart and so cannot collide with one another - but a
        // checker that silently ignores pending writes is a trap for the next caller, and the trap is
        // now club-wide rather than per-room.
        //
        // The excludingId term is UNREACHABLE here and kept for symmetry with the predicate above: an
        // Added entity has an id the caller has never seen, so it can never be the one being edited.
        // Dropping it would make the two halves read differently and invite the next reader to wonder
        // which one is right.
        return db.Classes.Local.Any(
            c => c.Status == ClassStatus.Scheduled
                 && (excludingId == null || c.Id != excludingId)
                 && db.Entry(c).State == EntityState.Added
                 && c.StartsAt < endsAt
                 && startsAt < c.StartsAt.AddMinutes(c.DurationMinutes));
    }
}
