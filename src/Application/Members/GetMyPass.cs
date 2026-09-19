using System.Security.Claims;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// The karnet covering TODAY, or 204 when the caller has none.
///
/// <para>
/// TODAY, NOT "THE LATEST". A member whose pass ran out last month has no valid karnet, and
/// answering with the expired one would put a card on their dashboard that reads like an
/// entitlement they do not have. The history is an admin concern; the member's question is "can I
/// train".
/// </para>
///
/// <para>
/// 204 RATHER THAN 404, for the reason <c>GetMyPlan</c> gives: holding no karnet is an ordinary
/// state, and a 404 would make the SPA guess whether the request failed or the answer is "nothing".
/// The dashboard renders an empty card for 204 and an error with a retry button for anything else,
/// which it can only do if the API tells the two apart.
/// </para>
///
/// <para>
/// The date is the CLUB's, not the server's and not the caller's browser's — see
/// <see cref="ClubTime"/>. At 01:00 local in summer the UTC date is still yesterday, and a pass
/// that expired yesterday would otherwise be reported as live for an hour every summer night.
/// </para>
/// </summary>
public static class GetMyPass
{
    public static async Task<IResult> HandleAsync(
        ClaimsPrincipal principal,
        IMembershipPassQuery query,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var memberId = principal.GetMemberId();
        if (memberId is null)
        {
            return Results.Unauthorized();
        }

        var today = DateOnly.FromDateTime(ClubTime.ToClubLocal(timeProvider.GetUtcNow()).DateTime);

        var pass = await query.FindCoveringAsync(memberId.Value, today, cancellationToken);

        return pass is null ? Results.NoContent() : Results.Ok(pass);
    }
}
