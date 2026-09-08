using Microsoft.EntityFrameworkCore;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
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

        // DRIVEN FROM MEMBERS since S-14, because that is what the selection submits — but the RULE
        // is unchanged and still runs through the account: an instructor needs an active login holding
        // the Trainer role. A member with no account is filtered out here by the inner join, which is
        // the read-side half of the refusal ClassEndpoints gives on write.
        //
        // Both statuses are checked. Either one can bar a person on its own since S-14, and offering a
        // name the server would then refuse is exactly what this filter exists to prevent.
        var rows = await db.Members
            .AsNoTracking()
            .Where(m => m.Status == MembershipStatus.Active)
            .Where(m => m.User != null && m.User.Status == AccountStatus.Active)
            .Where(m => db.UserRoles.Any(userRole =>
                userRole.UserId == m.UserId
                && db.Roles.Any(role =>
                    role.Id == userRole.RoleId && role.NormalizedName == normalized)))
            .OrderBy(m => m.DisplayName)
            .Select(m => new TrainerSummary(m.Id, m.DisplayName))
            .ToListAsync(cancellationToken);

        return rows;
    }
}
