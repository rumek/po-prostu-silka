using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// Read seam for karnety. Untracked and projected in the database, like every other <c>I*Query</c>
/// here — the write side is <see cref="IMembershipPassStore"/>.
/// </summary>
public interface IMembershipPassQuery
{
    /// <summary>
    /// This member's whole pass history, newest first, with entries used counted per pass.
    /// </summary>
    Task<IReadOnlyList<MembershipPassView>> GetForMemberAsync(
        Guid memberId,
        CancellationToken cancellationToken);

    /// <summary>
    /// The pass whose inclusive range contains <paramref name="clubLocalDate"/>, or null.
    ///
    /// <para>
    /// AT MOST ONE ROW CAN MATCH, because passes may not overlap — but that is an invariant enforced
    /// on the write path, not by this query, so the implementation still orders and takes one rather
    /// than assuming.
    /// </para>
    /// </summary>
    Task<MembershipPassView?> FindCoveringAsync(
        Guid memberId,
        DateOnly clubLocalDate,
        CancellationToken cancellationToken);
}
