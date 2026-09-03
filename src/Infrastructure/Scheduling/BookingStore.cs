using Microsoft.EntityFrameworkCore;
using po_prostu_silka.Application.Scheduling;
using po_prostu_silka.Domain.Scheduling;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Infrastructure.Scheduling;

/// <summary>
/// Infrastructure side of <see cref="IBookingStore"/>. Nothing here saves — the endpoint commits
/// through IUnitOfWork, which is what lets the booking insert and the class's stamp rotation land in
/// one SaveChangesAsync.
/// </summary>
public class BookingStore(AppDbContext db) : IBookingStore
{
    public void Add(Booking entity) => db.Bookings.Add(entity);

    public async Task<Booking?> FindActiveAsync(
        Guid classId, string memberUserId, CancellationToken cancellationToken) =>
        // Tracked, not AsNoTracking: the cancel path mutates what this returns.
        //
        // No Include. The two navigations exist for the read projections next door; a write path
        // reaching Class through this one could read Capacity without rotating the stamp, which is
        // the one mistake Booking's doc comment names.
        await db.Bookings.FirstOrDefaultAsync(
            b => b.ClassId == classId
                 && b.MemberUserId == memberUserId
                 && b.Status == BookingStatus.Active,
            cancellationToken);

    public async Task<Booking?> FindByIdAsync(
        Guid bookingId, CancellationToken cancellationToken) =>
        // Deliberately unfiltered by status and by class. The admin's release has to tell "no such
        // booking" from "that booking belongs to another class" from "already cancelled", and a
        // query that filtered here would collapse all three into a 404.
        await db.Bookings.FirstOrDefaultAsync(b => b.Id == bookingId, cancellationToken);

    public Task<int> CountActiveAsync(Guid classId, CancellationToken cancellationToken) =>
        // Seeks IX_Bookings_Class_Member_Active, whose filter is exactly this predicate's Status
        // term, so the count is an index seek rather than a table scan.
        db.Bookings.CountAsync(
            b => b.ClassId == classId && b.Status == BookingStatus.Active, cancellationToken);

    public async Task CancelActiveFutureForMemberAsync(
        string memberUserId, DateTimeOffset asOf, CancellationToken cancellationToken)
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
            .Where(b => b.MemberUserId == memberUserId
                        && b.Status == BookingStatus.Active
                        && b.Class.StartsAt > asOf)
            .ToListAsync(cancellationToken);

        foreach (var booking in future)
        {
            booking.Status = BookingStatus.Cancelled;
            booking.CancelledAt = asOf;
        }
    }
}
