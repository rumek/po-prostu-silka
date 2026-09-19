using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// The caller's own upcoming bookings - the member's read half after MP-01.
/// </summary>
public static class GetMyBookings
{
    /// <summary>
    /// The caller's upcoming bookings, chronological (prd.md FR-010).
    ///
    /// <para>
    /// UPCOMING ONLY, and the cut is by the class's start rather than by the booking's age. A member
    /// looking at "Moje zajęcia" is looking at what they still have to attend; the past belongs to
    /// history, which this slice keeps but does not display.
    /// </para>
    ///
    /// <para>
    /// DROPPED AN INJECTED UserManager IN S-18. It was bound on every request and never read: the
    /// caller is resolved from the principal's member claim, not from the account row.
    /// </para>
    /// </summary>
    public static async Task<IResult> HandleAsync(
        ClaimsPrincipal principal,
        IBookingQuery query,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var memberId = principal.GetMemberId();
        if (memberId is null)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(await query.GetUpcomingForMemberAsync(
            memberId.Value, timeProvider.GetUtcNow(), cancellationToken));
    }
}
