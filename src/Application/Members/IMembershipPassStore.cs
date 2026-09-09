using po_prostu_silka.Domain.Members;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// Narrow write seam over the <see cref="MembershipPass"/> table, so Application never references EF
/// Core (AGENTS.md layering). Implemented in Infrastructure.
///
/// <para>
/// TRACKED READS, like <see cref="IMemberStore"/> beside it and unlike the <c>I*Query</c> seams:
/// everything here is a step in a write, so the entity that comes back must be the one the unit of
/// work will save. Nothing here commits — the caller decides what lands in the same
/// <c>SaveChangesAsync</c>, which is what lets issuing a pass and rotating the member's stamp be one
/// atomic write.
/// </para>
/// </summary>
public interface IMembershipPassStore
{
    /// <summary>Stages a new pass. Commits nothing.</summary>
    void Add(MembershipPass pass);

    /// <summary>The pass with this id, tracked, or null.</summary>
    Task<MembershipPass?> FindAsync(Guid passId, CancellationToken cancellationToken);

    /// <summary>
    /// The first pass of this member whose inclusive range intersects <paramref name="from"/>..
    /// <paramref name="to"/>, tracked, or null when the range is free.
    ///
    /// <para>
    /// THE PROBE AND THE INSERT MUST SHARE ONE UNIT OF WORK, which is why this lives on the write seam
    /// rather than on a query. On its own it proves nothing: between the probe and the save, another
    /// admin can insert an overlapping pass. What makes it an invariant is that the caller rotates
    /// <see cref="Member.ConcurrencyStamp"/> in the same save, so the loser of that race fails the
    /// concurrency check instead of writing a second pass over the same days.
    /// </para>
    ///
    /// <para>
    /// Intersection is the inclusive test — <c>ValidFrom &lt;= to AND ValidTo &gt;= from</c> — so a
    /// range starting the day after another ends is NOT an overlap and is accepted.
    /// </para>
    /// </summary>
    /// <param name="excludingPassId">
    /// The pass being edited, so it does not collide with itself. Null when issuing a new one.
    /// </param>
    Task<MembershipPass?> FindOverlappingAsync(
        Guid memberId,
        DateOnly from,
        DateOnly to,
        Guid? excludingPassId,
        CancellationToken cancellationToken);

    /// <summary>
    /// The pass covering <paramref name="clubLocalDate"/> for this member, TRACKED, or null.
    ///
    /// <para>
    /// THE BOOKING GATE'S LOOKUP, and it is on the write seam rather than on
    /// <see cref="IMembershipPassQuery"/> for one reason: the booking path must ROTATE this pass's
    /// concurrency stamp in the same save as the insert, and an untracked projection cannot be
    /// rotated. The query's <c>FindCoveringAsync</c> is the read-only twin, for screens.
    /// </para>
    ///
    /// <para>
    /// At most one row can match while the non-overlap invariant holds — but that is enforced on the
    /// issue path, not here, so the implementation still orders and takes one.
    /// </para>
    /// </summary>
    Task<MembershipPass?> FindCoveringAsync(
        Guid memberId,
        DateOnly clubLocalDate,
        CancellationToken cancellationToken);

    /// <summary>
    /// Every pass, TRACKED, that any of <paramref name="passIds"/> names.
    ///
    /// <para>
    /// For the cancel paths, which must return an entry by rotating the stamp of each DISTINCT pass
    /// they touch. The block cascade releases a handful of bookings at once and they may sit on
    /// different passes, so this loads the set in one round trip rather than one per booking.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<MembershipPass>> FindManyAsync(
        IReadOnlyCollection<Guid> passIds,
        CancellationToken cancellationToken);

    /// <summary>Stages a delete. Commits nothing; the caller decides what else lands with it.</summary>
    void Remove(MembershipPass pass);
}
