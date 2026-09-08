using po_prostu_silka.Domain.Members;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// Narrow write seam over the <see cref="Member"/> table, so Application never references EF Core
/// (AGENTS.md layering). Implemented in Infrastructure.
///
/// <para>
/// TRACKED READS, unlike the <c>I*Query</c> seams beside it. Everything here is a step in a write —
/// claim a code, block a member, edit their details — so the entity that comes back must be the one the
/// unit of work will save. Nothing here commits: the caller decides what lands in the same
/// <c>SaveChangesAsync</c>, which is what lets a block release bookings and rotate stamps atomically.
/// </para>
/// </summary>
public interface IMemberStore
{
    /// <summary>Stages a new member. Commits nothing.</summary>
    void Add(Member member);

    /// <summary>The member with this id, tracked, or null.</summary>
    Task<Member?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// The member linked to this account, tracked, or null. Seeks IX_Members_UserId.
    /// </summary>
    Task<Member?> FindByUserIdAsync(string userId, CancellationToken cancellationToken);

    /// <summary>
    /// The member holding this access code, tracked, or null. Seeks IX_Members_AccessCode.
    /// </summary>
    /// <param name="code">
    /// NORMALISED, as MemberAccessCode.TryNormalise produces it. The stored form is the only form; a
    /// caller passing raw input would match nothing and answer "unknown code" for a code that exists.
    /// </param>
    Task<Member?> FindByAccessCodeAsync(string code, CancellationToken cancellationToken);
}
