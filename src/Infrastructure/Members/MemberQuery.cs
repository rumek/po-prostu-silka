using Microsoft.EntityFrameworkCore;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Domain;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Infrastructure.Members;

/// <summary>
/// Infrastructure side of <see cref="IMemberQuery"/> — the admin's full member list (FR-005).
/// Same shape as <see cref="PendingMemberQuery"/>: AsNoTracking, projected in the database, so the
/// list costs a few columns rather than whole Identity rows, and Application never sees EF Core.
///
/// <para>
/// ADMINS ARE NO LONGER EXCLUDED, and the protection that exclusion provided has MOVED rather than
/// vanished — read this before "restoring" the filter. It used to drop admins structurally, because
/// the seeded admin is an ordinary ApplicationUser in the same table and blocking the only admin
/// locks the club out of its own app. S-04 needs admins visible: prd-v2 FR-003 requires an owner who
/// teaches to be grantable the Trainer role, and the member list is the surface that grant lives on.
/// So the list now returns everyone, and the sole remaining guard is the is_admin check in
/// MemberAdminEndpoints.BlockAsync, which refuses the block itself. That check is by ROLE, so it
/// still holds if a second admin is ever seeded. The screen must not offer block on an admin row —
/// but the screen is not the boundary; that endpoint is.
/// </para>
/// </summary>
public class MemberQuery(AppDbContext db) : IMemberQuery
{
    public async Task<IReadOnlyList<MemberSummary>> GetMembersAsync(
        AccountStatus? status,
        CancellationToken cancellationToken)
    {
        var members = db.Users.AsNoTracking();

        if (status is not null)
        {
            // The indexed path (ApplicationUserConfiguration indexes Status for exactly this).
            members = members.Where(u => u.Status == status);
        }

        // Roles come back as a correlated collection projection. What this buys is ONE round-trip:
        // under EF's default SingleQuery behaviour the whole thing is a single statement (nothing in
        // this repo sets UseQuerySplittingBehavior). It does NOT avoid a join or a client-side
        // regroup - EF emits a LEFT JOIN and regroups in memory either way, which is fine at
        // one-club scale. If splitting is ever enabled globally this becomes two queries, still not
        // N+1; check the logged SQL if that day comes.
        //
        // Name, not NormalizedName — this is what crosses the wire to the screen, and Identity
        // stores the display form in Name. (Comparisons still use NormalizedName; see BlockAsync's
        // IsInRoleAsync, which normalises its argument.)
        var rows = await members
            .OrderBy(u => u.DisplayName)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.DisplayName,
                u.Status,
                u.CreatedAt,
                Roles = (from userRole in db.UserRoles
                         join role in db.Roles on userRole.RoleId equals role.Id
                         where userRole.UserId == u.Id
                         select role.Name).ToList(),
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
                r.Roles.Where(name => name is not null).Select(name => name!).ToList(),
                r.CreatedAt))
            .ToList();
    }
}
