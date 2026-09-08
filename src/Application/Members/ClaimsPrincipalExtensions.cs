using System.Security.Claims;
using po_prostu_silka.Domain;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// Reads the caller's identity out of their cookie.
///
/// <para>
/// WHY THIS EXISTS RATHER THAN A QUERY. The member id is minted into the principal by
/// <c>AppUserClaimsPrincipalFactory</c> alongside the membership status — the read that produces one
/// produces the other for free — so resolving "who is asking" costs nothing per request. Pure BCL,
/// no database, no injection.
/// </para>
///
/// <para>
/// IT ALSO KEEPS THE RULE THE BOOKING AND PLAN SURFACES HAVE ALWAYS FOLLOWED: the caller's identity
/// comes from the cookie, never from the request body. A handler that took a member id from its
/// payload would let any signed-in member book a spot for somebody else. The one endpoint that
/// legitimately names another member — the admin's book-on-behalf — is under the Admin policy and
/// says so explicitly.
/// </para>
/// </summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The caller's member id, or null when the claim is absent or unparseable.
    ///
    /// <para>
    /// Null is reachable only in the state the claims factory treats as a tripwire — an account with
    /// no member row — and every policy already refuses such a caller, so a handler behind
    /// <c>ActiveMember</c>, <c>Admin</c> or <c>TrainerOrAdmin</c> will not see it. Handlers under the
    /// bare <c>RequireAuthorization()</c> can, and must decide what to answer.
    /// </para>
    /// </summary>
    public static Guid? GetMemberId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(AuthorizationPolicyNames.MemberIdClaimType);

        return Guid.TryParse(value, out var id) ? id : null;
    }
}
