using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Application.Scheduling;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// The member list, one page at a time, optionally narrowed by the admin list filter and a search.
/// </summary>
public static class GetMembers
{
    /// <summary>What a caller that names no page size gets — the SPA's page, and the pager's step.</summary>
    public const int DefaultPageSize = 25;

    /// <summary>
    /// The largest page anyone may ask for. High enough for a picker that wants a screenful of
    /// matches, low enough that no caller can rebuild the old "everyone in one response" by asking.
    /// </summary>
    public const int MaxPageSize = 100;

    /// <summary>Longer than any name or e-mail a person would type to find someone.</summary>
    public const int MaxSearchLength = 100;

    /// <summary>
    /// One page of members, or of one filter position of them (FR-005). Admins ARE included since
    /// S-04 — prd-v2 FR-003 needs an owner who teaches to be grantable the Trainer role, and this list
    /// is the surface that grant lives on. Nothing here is a security boundary: the only thing
    /// stopping the club from blocking its own admin is BlockMember's is_admin check.
    ///
    /// <para>
    /// PAGED AND SEARCHED ON THE SERVER since S-21, reversing the earlier "no pagination, search is
    /// the SPA's job". That was right for a demo and wrong for a club: the browser fetched every
    /// member on every visit to show a screenful, and the class-bookings picker fetched the whole
    /// active club to fill one select. The SPA debounces typing, so a search costs one request per
    /// pause rather than one per keystroke.
    /// </para>
    /// </summary>
    public static async Task<IResult> HandleAsync(
        MemberListFilter? filter,
        string? search,
        int? page,
        int? pageSize,
        IMemberQuery query,
        CancellationToken cancellationToken)
    {
        // The bounds are MemberListRequest's, shared with the trainer's list (S-22).
        if (!MemberListRequest.TryRead(page, pageSize, search, out var request, out var refusal))
        {
            return refusal;
        }

        return Results.Ok(await query.GetMembersAsync(
            filter, request.Term, request.Page, request.PageSize, cancellationToken));
    }
}
