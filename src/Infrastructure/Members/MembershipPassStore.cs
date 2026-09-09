using Microsoft.EntityFrameworkCore;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Infrastructure.Members;

/// <summary>
/// Infrastructure side of <see cref="IMembershipPassStore"/>.
///
/// Tracked on purpose — see the interface. The scoped <see cref="AppDbContext"/> is the SAME instance
/// <see cref="MemberStore"/> uses, which is what lets an endpoint insert a pass and rotate the
/// member's concurrency stamp in one commit.
/// </summary>
public class MembershipPassStore(AppDbContext db) : IMembershipPassStore
{
    public void Add(MembershipPass pass) => db.MembershipPasses.Add(pass);

    public Task<MembershipPass?> FindAsync(Guid passId, CancellationToken cancellationToken) =>
        db.MembershipPasses.FirstOrDefaultAsync(x => x.Id == passId, cancellationToken);

    public Task<MembershipPass?> FindOverlappingAsync(
        Guid memberId,
        DateOnly from,
        DateOnly to,
        Guid? excludingPassId,
        CancellationToken cancellationToken) =>
        db.MembershipPasses
            .Where(x => x.MemberId == memberId)
            .Where(x => excludingPassId == null || x.Id != excludingPassId)
            // The inclusive intersection test. Note what it does NOT match: a range whose ValidFrom is
            // exactly the day after another's ValidTo, which is the normal case of renewing a karnet
            // and must stay legal.
            .Where(x => x.ValidFrom <= to && x.ValidTo >= from)
            .OrderBy(x => x.ValidFrom)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<MembershipPass?> FindCoveringAsync(
        Guid memberId,
        DateOnly clubLocalDate,
        CancellationToken cancellationToken) =>
        db.MembershipPasses
            .Where(x => x.MemberId == memberId)
            // The inclusive containment test, mirroring FindOverlappingAsync's inclusive
            // intersection. Seeks IX_MembershipPasses_MemberId_ValidFrom.
            .Where(x => x.ValidFrom <= clubLocalDate && clubLocalDate <= x.ValidTo)
            .OrderBy(x => x.ValidFrom)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<MembershipPass>> FindManyAsync(
        IReadOnlyCollection<Guid> passIds,
        CancellationToken cancellationToken)
    {
        if (passIds.Count == 0)
        {
            // Short-circuit rather than issuing "WHERE Id IN ()". The common cancel is of a booking
            // that carries no pass at all (a pre-S-16 row), and that case should cost no round trip.
            return [];
        }

        return await db.MembershipPasses
            .Where(x => passIds.Contains(x.Id))
            .ToListAsync(cancellationToken);
    }

    public void Remove(MembershipPass pass) => db.MembershipPasses.Remove(pass);
}
