using Microsoft.EntityFrameworkCore;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Domain;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Infrastructure.Members;

/// <summary>
/// Infrastructure side of <see cref="IPendingMemberQuery"/>. Exists so the admin endpoints can read
/// the pending queue without Application referencing EF Core, which AGENTS.md reserves for
/// Infrastructure.
///
/// <para>
/// DRIVEN FROM MEMBERS, not from accounts, even though the queue is accounts-only by definition — a
/// pending account has a login awaiting approval, so it necessarily has a member row too. Starting
/// here is what lets the projection hand back the member id every route on this surface is addressed
/// by. The inner join to the account is what applies the "pending" part.
/// </para>
///
/// Projected in the database — the admin queue needs five columns, not whole rows.
/// </summary>
public class PendingMemberQuery(AppDbContext db) : IPendingMemberQuery
{
    public async Task<IReadOnlyList<PendingMember>> GetPendingAsync(CancellationToken cancellationToken) =>
        await db.Members
            .AsNoTracking()
            .Where(m => m.User != null && m.User.Status == AccountStatus.Pending)
            // Oldest first: the person who has waited longest is the one to approve next.
            .OrderBy(m => m.CreatedAt)
            .Select(m => new PendingMember(
                m.Id,
                m.UserId!,
                m.Email ?? m.User!.Email ?? string.Empty,
                m.DisplayName,
                m.CreatedAt))
            .ToListAsync(cancellationToken);
}
