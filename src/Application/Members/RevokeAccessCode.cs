using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Application.Scheduling;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// Revokes a live member code (AM-004).
/// </summary>
public static class RevokeAccessCode
{
    /// <summary>Revokes the outstanding code. Idempotent — revoking nothing is a no-op, not an error.</summary>
    public static async Task<IResult> HandleAsync(
        Guid memberId,
        IMemberStore members,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var member = await members.FindAsync(memberId, cancellationToken);
        if (member is null)
        {
            return Results.NotFound();
        }

        if (member.AccessCode is null)
        {
            return Results.NoContent();
        }

        // BOTH FIELDS. The expiry is meaningless without the code and leaving it behind would make a
        // revoked member look, to any future reader, like one whose code merely lapsed.
        member.AccessCode = null;
        member.AccessCodeExpiresAt = null;
        member.ConcurrencyStamp = Guid.NewGuid().ToString();

        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            return Results.Json(new AccessCodeFailure("conflict"), statusCode: 409);
        }

        return Results.NoContent();
    }
}
