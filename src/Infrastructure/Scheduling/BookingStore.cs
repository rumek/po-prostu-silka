using Microsoft.EntityFrameworkCore;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Scheduling;
using po_prostu_silka.Domain.Scheduling;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Infrastructure.Scheduling;

/// <summary>
/// Infrastructure side of <see cref="IBookingStore"/>. Nothing here saves — the endpoint commits
/// through IUnitOfWork, which is what lets the booking insert and the class's stamp rotation land in
/// one SaveChangesAsync.
/// </summary>
public class BookingStore(AppDbContext db, IMembershipPassStore passes) : IBookingStore
{
    public void Add(Booking entity) => db.Bookings.Add(entity);

    public async Task<Booking?> FindActiveAsync(
        Guid classId, Guid memberId, CancellationToken cancellationToken) =>
        // Tracked, not AsNoTracking: the cancel path mutates what this returns.
        //
        // No Include. The two navigations exist for the read projections next door; a write path
        // reaching Class through this one could read Capacity without rotating the stamp, which is
        // the one mistake Booking's doc comment names.
        await db.Bookings.FirstOrDefaultAsync(
            b => b.ClassId == classId
                 && b.MemberId == memberId
                 && b.Status == BookingStatus.Active,
            cancellationToken);

    public async Task<Booking?> FindByIdAsync(
        Guid bookingId, CancellationToken cancellationToken) =>
        // Deliberately unfiltered by status and by class. The admin's release has to tell "no such
        // booking" from "that booking belongs to another class" from "already cancelled", and a
        // query that filtered here would collapse all three into a 404.
        await db.Bookings.FirstOrDefaultAsync(b => b.Id == bookingId, cancellationToken);

    public Task<int> CountActiveAsync(Guid classId, CancellationToken cancellationToken) =>
        // Seeks IX_Bookings_Class_MemberId_Active, whose filter is exactly this predicate's Status
        // term, so the count is an index seek rather than a table scan.
        db.Bookings.CountAsync(
            b => b.ClassId == classId && b.Status == BookingStatus.Active, cancellationToken);

    public Task<int> CountActiveForPassAsync(Guid passId, CancellationToken cancellationToken) =>
        // Seeks IX_Bookings_MembershipPassId_Status, which exists for this query specifically - it
        // runs inside the booking retry loop, on every attempt.
        db.Bookings.CountAsync(
            b => b.MembershipPassId == passId && b.Status == BookingStatus.Active, cancellationToken);

    public Task<bool> HasAnyAsync(Guid classId, CancellationToken cancellationToken) =>
        db.Bookings.AnyAsync(b => b.ClassId == classId, cancellationToken);

    public async Task CancelActiveFutureForMemberAsync(
        Guid memberId, DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        // TRACKED and materialised rather than an ExecuteUpdate: the caller (blocking a member) is
        // mid-way through its own unit of work, and the status flip, the outbox message and these
        // cancellations must land in the ONE SaveChangesAsync it already performs. ExecuteUpdate
        // writes immediately and outside that atom, which would leave a member blocked with their
        // bookings intact if the save then failed.
        //
        // The volume is bounded by how many future classes one person can be signed up for - a
        // handful - so materialising them costs nothing worth optimising.
        //
        // b.Class.StartsAt is a join in SQL, not a lazy load: this is IQueryable, and the navigation
        // is translated. Reading through it is safe here for the reason CancelActiveFutureForMember-
        // Async documents - cancelling only frees spots, so no stamp rotation is owed.
        var future = await db.Bookings
            .Where(b => b.MemberId == memberId
                        && b.Status == BookingStatus.Active
                        && b.Class.StartsAt > asOf)
            .ToListAsync(cancellationToken);

        foreach (var booking in future)
        {
            booking.Status = BookingStatus.Cancelled;
            booking.CancelledAt = asOf;
        }

        // THE ENTRIES GO BACK, AND THAT IS NOT SYMMETRIC WITH THE CLASS SPOTS ABOVE (S-16).
        //
        // This method rotates no Class stamp, and the doc comment on the interface says why: freeing
        // a spot is always conservative, so a concurrent booker reading a pre-cascade count cannot be
        // led into an overbooking. An ENTRY returning is the opposite kind of change - it makes a
        // booking possible that was refused a moment ago, which is a state another writer is actively
        // racing for. So each DISTINCT karnet these cancellations touch has its stamp rotated, in the
        // same unit of work the caller is about to commit.
        //
        // Distinct, because one member can hold several future bookings against one pass and EF would
        // otherwise be handed the same entity repeatedly - harmless, but the intent is one rotation
        // per pool, not one per booking.
        var passIds = future
            .Where(b => b.MembershipPassId != null)
            .Select(b => b.MembershipPassId!.Value)
            .Distinct()
            .ToList();

        // Through the store rather than a query of our own: it is the seam that owns loading passes
        // by id, it already short-circuits an empty set without a round trip, and a second copy of
        // this query here is one that a later change to the first would not reach.
        foreach (var pass in await passes.FindManyAsync(passIds, cancellationToken))
        {
            pass.ConcurrencyStamp = Guid.NewGuid().ToString();
        }

        // The cascade runs OUTSIDE a retry loop, so a lost race here surfaces to the caller as a
        // single 409 rather than being retried. That stays true and is acceptable for exactly the
        // reason it already was for the block itself: the admin refetches and presses again, and
        // nothing was half-written - the whole cascade is one SaveChangesAsync.
    }
}
