using Microsoft.EntityFrameworkCore;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Domain.Scheduling;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Infrastructure.Members;

/// <summary>
/// Infrastructure side of <see cref="IMembershipPassQuery"/> — the admin's karnet history and the
/// single-pass lookup the booking gate consults.
///
/// <para>
/// ENTRIES USED IS A CORRELATED SUBQUERY over active bookings, not a collection navigation. The same
/// technique the booking surface uses to count a class's occupied spots, and for the same reason
/// <see cref="Domain.Scheduling.Booking"/> spells out: a collection on the aggregate invites a write
/// path to count through it, and the subquery produces the same single SQL statement without the
/// hazard. It seeks IX_Bookings_MembershipPassId_Status.
/// </para>
///
/// <para>
/// AsNoTracking throughout — these are reads. Anything that consumes an entry goes through
/// <see cref="IMembershipPassStore"/> and rotates a stamp.
/// </para>
/// </summary>
public class MembershipPassQuery(AppDbContext db, TimeProvider timeProvider) : IMembershipPassQuery
{
    public async Task<IReadOnlyList<MembershipPassView>> GetForMemberAsync(
        Guid memberId,
        CancellationToken cancellationToken)
    {
        var today = ClubToday();

        var rows = await db.MembershipPasses
            .AsNoTracking()
            .Where(p => p.MemberId == memberId)
            // Newest first: the admin's eye goes to the current pass, and the history below it is
            // reference. Tie-broken on IssuedAt so two passes starting the same day (which the
            // non-overlap rule makes impossible today, but which a future edit could produce) still
            // have a stable order rather than the database's whim.
            .OrderByDescending(p => p.ValidFrom)
            .ThenByDescending(p => p.IssuedAt)
            .Select(p => new
            {
                p.Id,
                p.TypeName,
                p.ValidFrom,
                p.ValidTo,
                p.EntryCount,
                p.IssuedAt,
                EntriesUsed = db.Bookings.Count(b =>
                    b.MembershipPassId == p.Id && b.Status == BookingStatus.Active),
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new MembershipPassView(
                r.Id,
                r.TypeName,
                r.ValidFrom,
                r.ValidTo,
                r.EntryCount,
                r.EntriesUsed,
                r.EntryCount - r.EntriesUsed,
                r.IssuedAt,
                r.ValidFrom <= today && today <= r.ValidTo))
            .ToList();
    }

    public async Task<MembershipPassView?> FindCoveringAsync(
        Guid memberId,
        DateOnly clubLocalDate,
        CancellationToken cancellationToken)
    {
        var today = ClubToday();

        var row = await db.MembershipPasses
            .AsNoTracking()
            .Where(p => p.MemberId == memberId)
            // The inclusive containment test, mirroring the overlap probe's inclusive intersection.
            .Where(p => p.ValidFrom <= clubLocalDate && clubLocalDate <= p.ValidTo)
            // At most one row can match while the non-overlap invariant holds. Ordering anyway costs
            // nothing and means a hand-edited database yields a deterministic answer rather than an
            // arbitrary one.
            .OrderBy(p => p.ValidFrom)
            .Select(p => new
            {
                p.Id,
                p.TypeName,
                p.ValidFrom,
                p.ValidTo,
                p.EntryCount,
                p.IssuedAt,
                EntriesUsed = db.Bookings.Count(b =>
                    b.MembershipPassId == p.Id && b.Status == BookingStatus.Active),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        return new MembershipPassView(
            row.Id,
            row.TypeName,
            row.ValidFrom,
            row.ValidTo,
            row.EntryCount,
            row.EntriesUsed,
            row.EntryCount - row.EntriesUsed,
            row.IssuedAt,
            row.ValidFrom <= today && today <= row.ValidTo);
    }

    /// <summary>
    /// The date at the gym right now. Club-local rather than UTC because "does this pass cover today"
    /// is a question about the club's wall calendar — at 01:00 CEST the UTC date is still yesterday,
    /// and a pass that expired yesterday would be reported as live for an hour every summer night.
    /// </summary>
    private DateOnly ClubToday() =>
        DateOnly.FromDateTime(ClubTime.ToClubLocal(timeProvider.GetUtcNow()).DateTime);
}
