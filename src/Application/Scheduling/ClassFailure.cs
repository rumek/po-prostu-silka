using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// Why a class write was refused. All 400 except the six 409s — <c>time_conflict</c>,
/// <c>has_bookings</c>, <c>capacity_below_bookings</c>, <c>conflict</c>, <c>class_started</c> and
/// <c>already_cancelled</c> — each a disagreement with existing state rather than bad input.
///
/// <para>
/// Reasons: <c>missing_field</c>, <c>invalid_capacity</c>, <c>invalid_duration</c>,
/// <c>starts_in_past</c>, <c>invalid_weeks</c>, <c>time_conflict</c>, <c>unknown_class_type</c>,
/// <c>inactive_class_type</c>, <c>class_type_immutable</c>, <c>unknown_instructor</c>,
/// <c>instructor_not_trainer</c>, <c>has_bookings</c>, <c>capacity_below_bookings</c>,
/// <c>conflict</c>, <c>class_started</c>, <c>already_cancelled</c>. Adding one here means adding it
/// to the SPA's ClassFailure union too — that type mirrors this one field for field.
/// </para>
///
/// <para>
/// S-09 ADDED THE LAST TWO, both 409s and both belonging to <see cref="CancelAsync"/>.
/// <c>class_started</c> reuses the name BookingEndpoints already gives the same disagreement — the
/// class is no longer in the future — so the API speaks one vocabulary rather than two; it is a
/// refusal here because telling members a class that already happened is cancelled is
/// disinformation, and there is no undo. <c>already_cancelled</c> keeps the transition one-way and,
/// with the stamp rotation, keeps it exactly-once: two admins cancelling the same class must not
/// send two rounds of email.
/// </para>
///
/// <para>
/// S-08 ADDED THE LAST THREE, ALL 409s. <c>has_bookings</c> and <c>capacity_below_bookings</c> are
/// the two ways an admin action would otherwise break the no-overbooking guarantee from the
/// management side; <c>conflict</c> means a booking committed between this request's check and its
/// write, so the admin is asked to look again rather than shown a 500.
/// </para>
///
/// <para>
/// ONE MORE REASON TRAVELS IN THIS SHAPE WITHOUT BELONGING TO THAT UNION: <c>invalid_range</c>,
/// returned by the two READ endpoints when the requested date window is inverted or too wide. The
/// record is reused because the wire shape is identical, but no write path can ever return it — so
/// the SPA models it as its own <c>ScheduleReadFailure</c> rather than widening <c>ClassFailure</c>,
/// which would force the class form to carry a message for a refusal it cannot receive.
/// </para>
/// </summary>
public record ClassFailure(string Reason);
