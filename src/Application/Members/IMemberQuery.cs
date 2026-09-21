using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Paging;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Application.Scheduling;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// The seam for the full member list (FR-005): browses alphabetically and needs every status.
///
/// <para>
/// It used to be the SECOND such seam. IPendingMemberQuery was the other - the approvals queue,
/// ordered oldest first and carrying no status because every row in it had the same one - and S-16
/// deleted it along with approval itself. This one absorbed nothing in the process; it was always
/// the general case.
/// </para>
/// </summary>
public interface IMemberQuery
{
    /// <summary>
    /// One page of the list, filtered and searched HERE rather than in the SPA (S-21): at hundreds of
    /// members, shipping everyone to the browser to show 25 of them stopped being a cheap default.
    /// Ordered by display name, then id, so a page boundary never splits a tie differently twice.
    /// </summary>
    /// <param name="filter">Narrow to one filter position, or null for everyone.</param>
    /// <param name="search">
    /// A substring of the display name or e-mail, already trimmed; null for no search. Matched
    /// case- and accent-insensitively, <c>ł</c> included.
    /// </param>
    /// <param name="page">1-based; validated by the caller.</param>
    /// <param name="pageSize">Validated by the caller.</param>
    Task<PagedResult<MemberSummary>> GetMembersAsync(
        MemberListFilter? filter,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>One member with the fields the edit form needs, or null.</summary>
    Task<MemberDetail?> FindDetailAsync(Guid memberId, CancellationToken cancellationToken);

    /// <summary>
    /// Whether this address is already in use, by a member or by an account.
    /// </summary>
    /// <param name="exceptMemberId">
    /// The member being edited, so that saving a form without changing the address is not a
    /// collision with itself.
    /// </param>
    Task<bool> EmailExistsAsync(
        string email,
        Guid? exceptMemberId,
        CancellationToken cancellationToken);
}
