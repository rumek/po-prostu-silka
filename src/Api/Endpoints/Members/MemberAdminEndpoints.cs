using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Application.Scheduling;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Application.Members;

namespace po_prostu_silka.Api.Endpoints.Members;

/// <summary>
/// The admin's member surface: the full member list (S-02), the records of people who have never
/// registered at all (S-14), and the karnet screen those rows link to (S-16).
///
/// <para>
/// THE APPROVAL QUEUE THAT STARTED THIS FILE IS GONE (S-16, MP-03). S-01 built this surface around
/// <c>GET /pending</c> and <c>POST /{id}/approve</c>; both were removed once registration began
/// producing Active accounts, because the karnet — not a flag on the login — is what decides who may
/// train. Blocking survives unchanged and is now the only status lever here.
/// </para>
///
/// <para>
/// EVERY ROUTE HERE IS ADDRESSED BY MEMBER ID, including the ones that act on an account. That is a
/// deliberate uniformity: this screen's rows are people, half of them may have no account id to be
/// addressed by, and a surface where the identifier in the URL depended on which action you were
/// taking would be a mix-up waiting to happen. The account-shaped action that remains — the Trainer
/// role — resolves the member first and answers 409 <c>no_account</c> when there is nothing to act on.
/// </para>
///
/// <para>
/// WHY CONTACT DETAILS ARE ALL-OR-NOTHING HERE, AND REQUIRED AT REGISTRATION. FR-025 makes them
/// mandatory when someone registers, and that rule is unchanged. An admin recording a person at the
/// desk is a different situation: demanding a full postal address before the club may write down that
/// someone trains here would make this slice unusable for the case it exists for. So the block is
/// optional — but if ANY of the five is supplied, all five must be, validated by the same
/// <see cref="ContactDetails.TryCreate"/> the other two callers use. Half an address is the one
/// outcome nobody wants, and reusing the validator verbatim is what keeps the three surfaces from
/// drifting.
/// </para>
///
/// <para>
/// The PRD's open question about a blocked member's existing bookings is ANSWERED as of S-08:
/// blocking silently cancels the member's FUTURE bookings and leaves past ones alone. Since S-16 it
/// also returns the karnet entries those bookings were holding. Unblocking restores nothing.
/// </para>
///
/// The policy name comes from Domain (AuthorizationPolicyNames), not from Infrastructure's
/// AuthorizationPolicies, so this file holds no upward reference: Application -> Domain only.
/// </summary>
public static class MemberAdminEndpoints
{
    public static IEndpointRouteBuilder MapMemberAdminEndpoints(this IEndpointRouteBuilder app)
    {
        // The policy is applied at the GROUP, not per endpoint: an endpoint added here later cannot
        // accidentally ship unauthenticated. Admin implies Active, so a pending admin is refused too.
        var group = app.MapGroup("/api/admin/members")
            .WithTags("Members")
            .RequireAuthorization(AuthorizationPolicyNames.Admin);

        // GET /pending and POST /{id}/approve are GONE (S-16, MP-03). Approval no longer gates
        // anything: registration produces an Active account, and what decides whether somebody may
        // train is the karnet. Blocking is untouched and remains the admin's lever.
        group.MapGet("/", GetMembers.HandleAsync);
        group.MapGet("/{memberId:guid}", GetMember.HandleAsync);
        group.MapPost("/", CreateMember.HandleAsync);
        group.MapPut("/{memberId:guid}", UpdateMember.HandleAsync);
        group.MapPost("/{memberId:guid}/block", BlockMember.HandleAsync);
        group.MapPost("/{memberId:guid}/unblock", UnblockMember.HandleAsync);
        group.MapPost("/{memberId:guid}/roles/trainer", ChangeTrainerRole.GrantAsync);
        group.MapDelete("/{memberId:guid}/roles/trainer", ChangeTrainerRole.RevokeAsync);

        // A SEPARATE ROUTE FOR READING THE CODE, not a field on the member list. The list is loaded
        // on every visit to the members screen; shipping every live code into it would put the whole
        // set in the browser's memory and its network log for no reason. The list carries a boolean.
        group.MapGet("/{memberId:guid}/access-code", GetAccessCode.HandleAsync);
        group.MapPost("/{memberId:guid}/access-code", IssueAccessCode.HandleAsync);
        group.MapDelete("/{memberId:guid}/access-code", RevokeAccessCode.HandleAsync);

        return app;
    }
}
