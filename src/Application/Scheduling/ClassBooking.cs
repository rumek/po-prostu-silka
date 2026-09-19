using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// One signed-up member, as the admin's "Zapisani" panel shows them (prd.md FR-014).
///
/// <para>
/// The email is here because the club's actual use for this list is reaching people — a class is
/// moved, a trainer is ill — and it is admin-only surface, gated by the same policy as every other
/// admin endpoint. <see cref="MemberId"/> travels for the same reason it does on
/// <see cref="ScheduledClass"/>: the client needs a stable key, and it grants nothing on its own.
/// </para>
/// </summary>
/// <param name="MemberId">Who holds the spot.</param>
/// <param name="UserId">
/// Their account, or null when they have none (S-14).
///
/// <para>
/// IT TRAVELS BESIDE THE MEMBER ID BECAUSE PUSH IS ACCOUNT-KEYED and stays that way — a device
/// belongs to a login, not to a person, so the notification fan-out needs this to find the
/// subscriptions. Do not "simplify" it away by keying push on the member.
/// </para>
/// </param>
/// <param name="Email">
/// Where to reach them, or empty when the club has no address for them. Empty is a REAL case since
/// S-14 rather than the theoretical one it used to be: a person recorded at the desk may never have
/// given one, and they simply receive nothing.
/// </param>
public record ClassBooking(
    Guid BookingId,
    Guid MemberId,
    string? UserId,
    string DisplayName,
    string Email,
    DateTimeOffset BookedAt);
