using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// The write seam over the booking table, so Application does not reference EF Core (AGENTS.md
/// layering). Implemented in Infrastructure.
///
/// <para>
/// Nothing here saves — the endpoint commits through <see cref="IUnitOfWork"/>. That is what lets the
/// booking insert and the class's stamp rotation land in ONE SaveChangesAsync, which is the entire
/// no-overbooking design.
/// </para>
///
/// <para>
/// Intention-revealing methods rather than a generic repository; this codebase has no repository
/// pattern and this slice does not introduce one.
/// </para>
/// </summary>
public interface IBookingStore
{
    void Add(Booking entity);

    /// <summary>
    /// The member's active booking on this class, or null. TRACKED — the cancel path mutates what
    /// this returns and expects the change tracker to notice.
    /// </summary>
    Task<Booking?> FindActiveAsync(
        Guid classId, Guid memberId, CancellationToken cancellationToken);

    /// <summary>
    /// One booking by id, TRACKED, for the admin's release. Returns it whatever its status and
    /// whichever class it belongs to — the caller checks both, because "wrong class" and "already
    /// cancelled" are answers it has to distinguish.
    /// </summary>
    Task<Booking?> FindByIdAsync(Guid bookingId, CancellationToken cancellationToken);

    /// <summary>
    /// How many spots this class currently has taken.
    ///
    /// <para>
    /// THERE IS NO STORED COUNTER; the count IS the number of active rows. Reading it is only half of
    /// a capacity check — the other half is rotating <see cref="Class.ConcurrencyStamp"/> before
    /// saving, without which this number is stale by the time it is acted on.
    /// </para>
    /// </summary>
    Task<int> CountActiveAsync(Guid classId, CancellationToken cancellationToken);

    /// <summary>
    /// How many entries of this karnet are currently spent or reserved (S-16, S-27).
    ///
    /// <para>
    /// THERE IS NO STORED COUNTER here either; entries used IS the number of bookings carrying this
    /// pass's id that still consume an entry — active, on a class that was not cancelled, and not
    /// marked absent. The one definition of that is EntryConsumption in Infrastructure, shared with
    /// both read paths so the gate and the displayed balance cannot drift. Reading it is only half of an entry check — the other half is rotating
    /// <see cref="Domain.Members.MembershipPass.ConcurrencyStamp"/> before saving, without which this
    /// number is stale by the time it is acted on. Exactly the shape
    /// <see cref="CountActiveAsync"/> has, one pool up.
    /// </para>
    ///
    /// <para>
    /// Seeks IX_Bookings_MembershipPassId_Status. It runs on EVERY attempt of the booking retry loop,
    /// so it must be a seek.
    /// </para>
    /// </summary>
    Task<int> CountConsumingForPassAsync(Guid passId, CancellationToken cancellationToken);

    /// <summary>
    /// Whether any ACTIVE booking carries this pass's id, whatever its attendance or its class's
    /// status (S-27).
    ///
    /// <para>
    /// The revoke guard, and deliberately NOT the entries-used count: a booking marked absent returns
    /// its entry but is still history recording which karnet paid, and the restrict foreign key would
    /// refuse the delete anyway.
    /// </para>
    /// </summary>
    Task<bool> AnyActiveForPassAsync(Guid passId, CancellationToken cancellationToken);

    /// <summary>
    /// Rotates the stamp of every distinct karnet an active booking on this class carries, without
    /// saving (S-27).
    ///
    /// <para>
    /// For cancelling a class: its bookings stop consuming entries, which makes a booking possible
    /// that was refused a moment ago — the case <see cref="BookingProtocol.ReturnEntryAsync"/>
    /// describes, once per pool.
    /// </para>
    /// </summary>
    Task RotatePassStampsForClassAsync(Guid classId, CancellationToken cancellationToken);

    /// <summary>
    /// Whether this class has EVER been booked, cancelled bookings included.
    ///
    /// <para>
    /// The delete guard, and deliberately wider than <see cref="CountActiveAsync"/>. Both FKs on
    /// Bookings are RESTRICT, so a class with any booking row at all cannot be deleted by the
    /// database either — a guard that counted only active rows would answer "go ahead" and then let
    /// the save fail with a foreign-key violation. Widening it also states the product rule
    /// honestly: DELETE erases a class created by mistake, and a class somebody once signed up for
    /// is not one.
    /// </para>
    /// </summary>
    Task<bool> HasAnyAsync(Guid classId, CancellationToken cancellationToken);

    /// <summary>
    /// Marks every active booking this member holds on a class starting after
    /// <paramref name="asOf"/> as cancelled, without saving.
    ///
    /// <para>
    /// For the block cascade: a blocked member cannot attend, so the club must not keep promising
    /// their seats to nobody. PAST bookings are deliberately untouched — they are attendance history,
    /// and rewriting them would be falsifying it.
    /// </para>
    ///
    /// <para>
    /// Does not save, and does not rotate any class stamp. Safe with respect to capacity because
    /// cancelling only ever FREES spots: a concurrent booker reading a pre-cascade count is being
    /// conservative, never permissive.
    /// </para>
    /// </summary>
    Task CancelActiveFutureForMemberAsync(
        Guid memberId, DateTimeOffset asOf, CancellationToken cancellationToken);
}
