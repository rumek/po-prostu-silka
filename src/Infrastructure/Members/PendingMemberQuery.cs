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
/// Filtered on Status, which ApplicationUserConfiguration already indexes, and projected in the
/// database — the admin queue needs four columns, not whole Identity rows.
/// </summary>
public class PendingMemberQuery(AppDbContext db) : IPendingMemberQuery
{
    public async Task<IReadOnlyList<PendingMember>> GetPendingAsync(CancellationToken cancellationToken) =>
        await db.Users
            .AsNoTracking()
            .Where(u => u.Status == AccountStatus.Pending)
            // Oldest first: the person who has waited longest is the one to approve next.
            .OrderBy(u => u.CreatedAt)
            .Select(u => new PendingMember(
                u.Id,
                u.Email ?? string.Empty,
                u.DisplayName,
                u.CreatedAt))
            .ToListAsync(cancellationToken);
}
