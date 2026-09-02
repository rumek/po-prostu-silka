using Microsoft.EntityFrameworkCore;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Domain;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Infrastructure.Members;

/// <summary>
/// Infrastructure side of <see cref="ITrainerQuery"/> — the instructor selection's source
/// (prd-v2 FR-009). Same shape as <see cref="MemberQuery"/>: AsNoTracking, projected in the database,
/// so the list costs two columns rather than whole Identity rows, and Application never sees EF Core.
/// </summary>
public class TrainerQuery(AppDbContext db) : ITrainerQuery
{
    public async Task<IReadOnlyList<TrainerSummary>> GetActiveTrainersAsync(
        CancellationToken cancellationToken)
    {
        // NormalizedName, not Name. Identity stores the display form in Name and the comparison form
        // in NormalizedName, and it is the normalized column that is indexed and unique. Matching on
        // Name would work today only because the seeder happens to write "Trainer" exactly, and would
        // break the day a role is created through any path that cases it differently.
        var normalized = ApplicationRoles.Trainer.ToUpperInvariant();

        var rows = await db.Users
            .AsNoTracking()
            // The active filter is the read-side half of the rule ClassEndpoints enforces on write:
            // offering a blocked trainer would let the admin pick a name the server then refuses.
            // Indexed by ApplicationUserConfiguration's Status index.
            .Where(u => u.Status == AccountStatus.Active)
            .Where(u => db.UserRoles.Any(userRole =>
                userRole.UserId == u.Id
                && db.Roles.Any(role =>
                    role.Id == userRole.RoleId && role.NormalizedName == normalized)))
            .OrderBy(u => u.DisplayName)
            .Select(u => new TrainerSummary(u.Id, u.DisplayName))
            .ToListAsync(cancellationToken);

        return rows;
    }
}
