using System.Linq.Expressions;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Infrastructure.Scheduling;

/// <summary>
/// THE ONE DEFINITION of whether a booking consumes a karnet entry (S-27, AT-03 amending MP-06).
///
/// <para>
/// A booking with a pass consumes an entry unless it was released, its class was cancelled, or it
/// was marked absent. That gives three readings of one row:
/// <list type="bullet">
/// <item><b>reserved</b> — a future booking, or a past one nobody has marked;</item>
/// <item><b>spent</b> — marked present, or never marked;</item>
/// <item><b>returned</b> — marked absent, released, or its class was cancelled.</item>
/// </list>
/// </para>
///
/// <para>
/// UNRECORDED COUNTS AS SPENT, and that is what keeps the no-overdraw guarantee without a window or a
/// background job: every entry a future booking might spend is already held from the moment of
/// booking, so no sequence of writes can promise one entry twice. It is also today's meaning, which
/// is why the column needed no backfill.
/// </para>
///
/// <para>
/// ONE EXPRESSION, THREE SITES: the booking gate (<see cref="BookingStore.CountConsumingForPassAsync"/>)
/// and both read paths in MembershipPassQuery. If they drift, a member is refused while their card
/// shows an entry left, or the reverse. It is an expression rather than a method so EF Core inlines it
/// into a correlated subquery; a method call in the tree would not translate.
/// </para>
///
/// <para>
/// <c>Attendance != Absent</c> must keep NULL rows. EF Core's default relational null semantics emit
/// <c>IS NULL OR &lt;&gt; 1</c>; do not switch on UseRelationalNulls to "optimise" it.
/// </para>
/// </summary>
public static class EntryConsumption
{
    public static readonly Expression<Func<Booking, bool>> ConsumesAnEntry = b =>
        b.Status == BookingStatus.Active
        && b.Class.Status != ClassStatus.Cancelled
        && b.Attendance != BookingAttendance.Absent;
}
