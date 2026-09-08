using Microsoft.EntityFrameworkCore;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Infrastructure.Members;

/// <summary>
/// Infrastructure side of <see cref="IMemberStore"/>.
///
/// Tracked on purpose — see the interface. The scoped <see cref="AppDbContext"/> is the SAME instance
/// Identity's stores use, which is what lets an endpoint change a member and its linked account in one
/// commit.
/// </summary>
public class MemberStore(AppDbContext db) : IMemberStore
{
    public void Add(Member member) => db.Members.Add(member);

    public Task<Member?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Members.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<Member?> FindByUserIdAsync(string userId, CancellationToken cancellationToken) =>
        db.Members.FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);

    public Task<Member?> FindByAccessCodeAsync(string code, CancellationToken cancellationToken) =>
        db.Members.FirstOrDefaultAsync(x => x.AccessCode == code, cancellationToken);
}
