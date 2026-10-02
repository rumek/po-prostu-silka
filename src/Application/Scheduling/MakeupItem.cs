namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// One row of the staff "Odrabianie" list (S-36): an absence marked "odrobi" and what became of it.
/// </summary>
/// <param name="AbsenceBookingId">The absence booking — the item's identity on every makeup route.</param>
/// <param name="Deadline">The last club-local date a makeup class may take place on. Inclusive.</param>
/// <param name="Status"><c>open</c>, <c>planned</c>, <c>made_up</c> or <c>not_made_up</c>.</param>
/// <param name="ClosedByHand">Staff closed it as "nie odrobił"; it may be reopened within the deadline.</param>
/// <param name="Makeup">The live makeup booking, or null when none stands.</param>
public record MakeupItem(
    Guid AbsenceBookingId,
    Guid MemberId,
    string DisplayName,
    string ClassName,
    DateTimeOffset AbsenceStartsAt,
    string Instructor,
    DateOnly Deadline,
    string Status,
    bool ClosedByHand,
    MakeupClass? Makeup);

/// <summary>The class a makeup is booked on.</summary>
public record MakeupClass(
    Guid ClassId,
    Guid BookingId,
    string Name,
    DateTimeOffset StartsAt,
    string Instructor);

/// <summary>What the member is told on Moje zajęcia: how many items are open, and the nearest deadline.</summary>
public record MyMakeups(int Count, DateOnly? NearestDeadline);

/// <summary>
/// Why a makeup write was refused (S-36). Every one is a 409, for <see cref="BookingFailure"/>'s
/// reason: nothing in these requests can be malformed, only out of step with state.
///
/// <para>
/// Reasons: <c>makeup_not_open</c> (the item is planned, made up, not made up or closed),
/// <c>makeup_deadline_passed</c> (the chosen class falls after the deadline),
/// <c>makeup_not_reopenable</c> (reopen of an item not closed by hand, or past its deadline), and the
/// booking loop's own <c>class_full</c>, <c>class_started</c>, <c>class_cancelled</c>,
/// <c>already_booked</c>, <c>no_valid_pass</c>, <c>member_blocked</c>, <c>member_is_staff</c>,
/// <c>conflict</c>. The SPA's MakeupFailure union carries exactly these.
/// </para>
///
/// <para>
/// THE SAME WIRE SHAPE AS <see cref="BookingFailure"/> — <c>{ "reason": … }</c> — because the makeup
/// booking runs through <see cref="BookingProtocol.TryBookAsync"/>, which answers with that record. One
/// shape keeps the SPA's classifier one function.
/// </para>
/// </summary>
public record MakeupFailure(string Reason)
{
    public static IResult Refuse(string reason) => Results.Json(new MakeupFailure(reason), statusCode: 409);
}
