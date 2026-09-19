using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// Why a booking write was refused.
///
/// <para>
/// EVERY ONE OF THESE IS A 409, which is what makes this union different from
/// <see cref="ClassFailure"/>. A booking request carries no fields to get wrong — the class is in the
/// route and the member is named by the caller's own request — so there is nothing here that could be
/// a 400. What can fail is always a disagreement with state the caller could not see: the class was
/// cancelled, it has started, the person already holds a spot, the spots ran out, their karnet does
/// not cover the day or has no entries left.
/// </para>
///
/// <para>
/// Reasons: <c>class_cancelled</c>, <c>class_started</c>, <c>already_booked</c>, <c>class_full</c>,
/// <c>member_blocked</c>, <c>no_valid_pass</c>, <c>no_entries_left</c>, <c>conflict</c>. Adding one
/// means adding it to the SPA's BookingFailure union too. A missing class is a 404 and not a reason —
/// an unknown id is not a state disagreement, and neither is a member id nobody issued.
/// </para>
///
/// <para>
/// <c>not_booked</c> IS GONE as of S-16 and must not come back. It belonged to the member's own
/// cancel route, which produced it when there was no spot to release; the staff release addresses a
/// booking by id and answers 404 for one that does not exist, belongs to another class, or is already
/// cancelled — see <c>ReleaseAsync</c> for why those three collapse.
/// </para>
///
/// <para>
/// THE TWO KARNET REFUSALS ARE DIFFERENT QUESTIONS AND MUST STAY DISTINGUISHABLE (S-16).
/// <c>no_valid_pass</c> means the member holds no pass covering the class's club-local date — either
/// they have none at all, or the one they have does not reach that day; those two are indistinguishable
/// from the lookup's side and are deliberately one reason, because the club's answer to both is "sell
/// them a karnet". <c>no_entries_left</c> means a pass DOES cover the date and its entries are all
/// spent, which is a different conversation at the desk.
/// </para>
///
/// <para>
/// <c>member_blocked</c> exists because the staff route names its member in the BODY, where no policy
/// can vouch for them (S-14). It used to be documented as impossible on the member's own route; that
/// route is gone, so it is now simply one of the ordinary refusals.
/// </para>
///
/// <para>
/// <c>conflict</c> is the only one that is not a product rule: it means the retry loop lost its race
/// on every one of its attempts, which for a club of dozens should never happen. It exists so the
/// caller is told to try again rather than shown a 500.
/// </para>
/// </summary>
public record BookingFailure(string Reason);
