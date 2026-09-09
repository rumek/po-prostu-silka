using System.Security.Claims;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// The member's own karnet (S-16, MP-07) — the read half of the pass, and the only pass surface a
/// member ever sees.
///
/// <para>
/// SCOPED TO THE CALLER, AND THERE IS NO ID TO TAMPER WITH. The route takes no member id at all: it
/// resolves the caller from the cookie and asks about them. That is the same shape
/// <see cref="Training.MyPlanEndpoints"/> uses and it is stronger than an ownership comparison —
/// there is no parameter to get wrong, so there is no way to get it wrong.
/// </para>
///
/// <para>
/// READ ONLY, and that is the product decision rather than a scope cut (MP-01). A member does not
/// buy, extend or request a karnet in this app; they read what the club issued them. The screen shows
/// type, validity and entries left, and nothing on it is a button.
/// </para>
///
/// <para>
/// SEPARATE FROM <see cref="MembershipPassEndpoints"/> on purpose, rather than one more route on that
/// group. The two answer to different policies — ActiveMember here, Admin there — and this codebase
/// applies one policy per group.
/// </para>
/// </summary>
public static class MyPassEndpoints
{
    public static IEndpointRouteBuilder MapMyPassEndpoints(this IEndpointRouteBuilder app)
    {
        var mine = app.MapGroup("/api/passes")
            .WithTags("MembershipPasses")
            .RequireAuthorization(AuthorizationPolicyNames.ActiveMember);

        mine.MapGet("/mine", GetMineAsync);

        return app;
    }

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
    /// 204 RATHER THAN 404, for the reason <c>MyPlanEndpoints.GetMineAsync</c> gives: holding no
    /// karnet is an ordinary state, and a 404 would make the SPA guess whether the request failed or
    /// the answer is "nothing". The dashboard renders an empty card for 204 and an error with a retry
    /// button for anything else, which it can only do if the API tells the two apart.
    /// </para>
    ///
    /// <para>
    /// The date is the CLUB's, not the server's and not the caller's browser's — see
    /// <see cref="ClubTime"/>. At 01:00 local in summer the UTC date is still yesterday, and a pass
    /// that expired yesterday would otherwise be reported as live for an hour every summer night.
    /// </para>
    /// </summary>
    private static async Task<IResult> GetMineAsync(
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
