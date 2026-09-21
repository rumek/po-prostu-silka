using System.Diagnostics.CodeAnalysis;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// One member-list request, bounded and normalized — the paging and search rules every member list
/// shares (S-21, S-22).
///
/// <para>
/// ONE VALIDATOR FOR BOTH LISTS. The admin's list and the trainer's list refuse exactly the same
/// input with exactly the same <see cref="MemberListFailure"/> reasons, so the SPA's one failure table
/// answers for both. It lives in Members rather than Paging because it speaks
/// <see cref="MemberListFailure"/>; Paging stays free of any bounded context.
/// </para>
/// </summary>
/// <param name="Page">1-based.</param>
/// <param name="PageSize">1–<see cref="GetMembers.MaxPageSize"/>.</param>
/// <param name="Term">The trimmed search phrase, or null for "no search".</param>
public sealed record MemberListRequest(int Page, int PageSize, string? Term)
{
    /// <summary>
    /// Reads the raw query values, or answers the 400 a caller gets for asking outside the bounds.
    /// </summary>
    public static bool TryRead(
        int? page,
        int? pageSize,
        string? search,
        [NotNullWhen(true)] out MemberListRequest? request,
        [NotNullWhen(false)] out IResult? refusal)
    {
        request = null;
        refusal = null;

        var pageNumber = page ?? 1;
        var size = pageSize ?? GetMembers.DefaultPageSize;

        // The offset is (page - 1) * size in int arithmetic further down; a page far enough past any
        // real list would wrap it negative and turn a caller's typo into a SQL error — a 500.
        if (pageNumber < 1
            || size < 1
            || size > GetMembers.MaxPageSize
            || (long)(pageNumber - 1) * size > int.MaxValue)
        {
            refusal = Results.Json(new MemberListFailure("invalid_page"), statusCode: 400);
            return false;
        }

        // Whitespace is "no search", not a search for spaces: a box cleared with the space bar
        // should show the list, not an empty result.
        var term = string.IsNullOrWhiteSpace(search) ? null : search.Trim();

        if (term is { Length: > GetMembers.MaxSearchLength })
        {
            refusal = Results.Json(new MemberListFailure("invalid_search"), statusCode: 400);
            return false;
        }

        request = new MemberListRequest(pageNumber, size, term);
        return true;
    }
}
