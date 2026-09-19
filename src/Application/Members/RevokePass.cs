using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// Revokes an issued karnet (MP-01).
/// </summary>
public static class RevokePass
{
    /// <summary>
    /// Removes a karnet that should never have been issued.
    ///
    /// <para>
    /// REFUSED WHILE ANY BOOKING STILL POINTS AT IT. A pass that paid for a spot cannot vanish
    /// underneath it — the restrict foreign key would refuse the delete anyway, and answering that as
    /// a 500 rather than a 409 with a reason the admin can act on would be the difference between a
    /// screen that explains itself and one that appears broken. To retire a live pass the admin
    /// releases the bookings first, which is a decision about people's spots and should be a
    /// deliberate act.
    /// </para>
    /// </summary>
    public static async Task<IResult> HandleAsync(
        Guid memberId,
        Guid passId,
        IMemberStore members,
        IMembershipPassStore passes,
        IMembershipPassQuery query,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        if (await members.FindAsync(memberId, cancellationToken) is null)
        {
            return Results.NotFound();
        }

        var pass = await passes.FindAsync(passId, cancellationToken);
        if (pass is null || pass.MemberId != memberId)
        {
            return Results.NotFound();
        }

        var used = await MembershipPassProjection.EntriesUsedAsync(query, memberId, passId, cancellationToken);

        if (used > 0)
        {
            return Results.Json(new MembershipPassFailure("has_active_bookings"), statusCode: 409);
        }

        passes.Remove(pass);

        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            return Results.Json(new MembershipPassFailure("conflict"), statusCode: 409);
        }

        return Results.NoContent();
    }
}
