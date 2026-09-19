using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Application.Scheduling;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// The member list, optionally narrowed by the admin list filter.
/// </summary>
public static class GetMembers
{
    /// <summary>
    /// Every member, or one filter position of them (FR-005). Admins ARE included since S-04 — prd-v2
    /// FR-003 needs an owner who teaches to be grantable the Trainer role, and this list is the
    /// surface that grant lives on. Nothing here is a security boundary: the only thing stopping the
    /// club from blocking its own admin is <see cref="BlockAsync"/>'s is_admin check.
    ///
    /// No pagination, for the reason GetPendingAsync gives. Search is the SPA's job — it filters the
    /// loaded rows, which is instant and costs no round-trip.
    /// </summary>
    public static async Task<IResult> HandleAsync(
        MemberListFilter? filter,
        IMemberQuery query,
        CancellationToken cancellationToken) =>
        Results.Ok(await query.GetMembersAsync(filter, cancellationToken));
}
