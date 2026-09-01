using Microsoft.EntityFrameworkCore;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Domain;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Infrastructure.Members;

/// <summary>
/// Infrastructure side of <see cref="IMemberQuery"/> — the admin's full member list (FR-005).
/// Same shape as <see cref="PendingMemberQuery"/>: AsNoTracking, projected in the database, so the
/// list costs five columns rather than whole Identity rows, and Application never sees EF Core.
///
/// <para>
/// ADMINS ARE EXCLUDED STRUCTURALLY, and this is load-bearing, not tidiness. The seeded admin is an
/// ordinary ApplicationUser with Status = Active in the same table (AdminSeeder), so a query
/// filtered on Status alone — which is exactly what PendingMemberQuery does — would list the admin
/// as a blockable member. With one admin ever seeded, blocking them locks the club out of its own
/// app with no way back in through the UI. The exclusion is by ROLE rather than by user id so it
/// still holds if a second admin is ever seeded.
/// </para>
/// </summary>
public class MemberQuery(AppDbContext db) : IMemberQuery
{
    public async Task<IReadOnlyList<MemberSummary>> GetMembersAsync(
        AccountStatus? status,
        CancellationToken cancellationToken)
    {
        var adminIds =
            from userRole in db.UserRoles
            join role in db.Roles on userRole.RoleId equals role.Id
            where role.Name == ApplicationRoles.Admin
            select userRole.UserId;

        var members = db.Users
            .AsNoTracking()
            .Where(u => !adminIds.Contains(u.Id));

        if (status is not null)
        {
            // The indexed path (ApplicationUserConfiguration indexes Status for exactly this).
            members = members.Where(u => u.Status == status);
        }

        // Alphabetical: this is a browse-and-find surface, unlike the pending queue, where oldest
        // first is the whole point.
        var rows = await members
            .OrderBy(u => u.DisplayName)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.DisplayName,
                u.Status,
                u.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        // Enum -> name after materialising: ToString() on an enum has no SQL translation, and
        // forcing one would cost more than mapping a short list in memory.
        return rows
            .Select(r => new MemberSummary(
                r.Id,
                r.Email ?? string.Empty,
                r.DisplayName,
                r.Status.ToString(),
                r.CreatedAt))
            .ToList();
    }
}
