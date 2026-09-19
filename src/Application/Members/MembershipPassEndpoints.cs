using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// The admin's karnet surface (S-16, MP-04..MP-06): issue a pass, read a member's history, correct a
/// mistake, revoke one that should not have been issued.
///
/// <para>
/// NESTED UNDER THE MEMBER, not a top-level /api/admin/passes. A karnet has no existence apart from
/// the person holding it, and every screen that reaches these routes arrives from a member row — so
/// the member id belongs in the path rather than in a query string, and every handler here verifies
/// that the pass it was addressed with actually belongs to the member in the URL. Without that check
/// the nesting would be decoration and <c>/members/{someone-else}/passes/{id}</c> would work.
/// </para>
///
/// <para>
/// WHY NON-OVERLAP IS CHECKED HERE AND NOT BY AN INDEX. "No two of this member's ranges intersect" is
/// a rule about PAIRS OF ROWS, and SQL Server has no index shape that expresses it — unlike
/// IX_Members_AccessCode, which is a single-column uniqueness rule and can be. So the probe runs in
/// the handler, and what makes it an invariant rather than a guess is that the insert rotates
/// <see cref="Member.ConcurrencyStamp"/> in the same save: two admins issuing overlapping passes at
/// the same instant both pass the probe, and exactly one of them commits. That is the same protocol
/// claiming an access code and blocking already use, applied to a new shape.
/// </para>
///
/// <para>
/// The policy name comes from Domain (<see cref="AuthorizationPolicyNames"/>), not from
/// Infrastructure's AuthorizationPolicies, so this file holds no upward reference: Application ->
/// Domain only.
/// </para>
/// </summary>
public static class MembershipPassEndpoints
{
    public static IEndpointRouteBuilder MapMembershipPassEndpoints(this IEndpointRouteBuilder app)
    {
        // The policy is applied at the GROUP, not per endpoint: an endpoint added here later cannot
        // accidentally ship unauthenticated. Admin implies Active.
        var group = app.MapGroup("/api/admin/members/{memberId:guid}/passes")
            .WithTags("MembershipPasses")
            .RequireAuthorization(AuthorizationPolicyNames.Admin);

        group.MapGet("/", GetPasses.HandleAsync);
        group.MapPost("/", IssuePass.HandleAsync);
        group.MapPut("/{passId:guid}", UpdatePass.HandleAsync);
        group.MapDelete("/{passId:guid}", RevokePass.HandleAsync);

        return app;
    }
}
