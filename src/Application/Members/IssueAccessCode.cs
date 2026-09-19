using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Application.Scheduling;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// Issues a single-use, expiring member code (AM-004).
/// </summary>
public static class IssueAccessCode
{
    /// <summary>
    /// How many times generation retries after colliding with a live code.
    ///
    /// Three is plenty and is not a concurrency bound like the booking loop's: the collision it guards
    /// against is 1-in-8.5×10¹¹ per attempt, so exhausting this means the random source is broken
    /// rather than that the club is busy.
    /// </summary>
    private const int CodeAttempts = 3;

    /// <summary>
    /// Issues a member code (AM-004), replacing any outstanding one.
    ///
    /// <para>
    /// REPLACING RATHER THAN REFUSING is deliberate: the admin pressing this again is someone who
    /// could not find the code they wrote down, and making them revoke first would be ceremony. The
    /// previous code stops working the moment this one is stored, which is the honest behaviour — one
    /// live code per member, always.
    /// </para>
    /// </summary>
    public static async Task<IResult> HandleAsync(
        Guid memberId,
        IMemberStore members,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var member = await members.FindAsync(memberId, cancellationToken);
        if (member is null)
        {
            return Results.NotFound();
        }

        // Nothing to claim. The code's entire power is "attach the account being created to this
        // member", and this member already has one.
        if (member.UserId is not null)
        {
            return Results.Json(new AccessCodeFailure("has_account"), statusCode: 409);
        }

        // THE SCREEN IS NOT THE BOUNDARY, same rule BlockAsync states about the is_admin refusal. The
        // members list does hide this action on a blocked row, but a code minted for someone the club
        // has blocked is a code that cannot work — RegisterAsync refuses it — and handing the admin
        // one to read out is the same disservice GetAccessCodeAsync refuses to do for an expired code.
        if (member.Status != MembershipStatus.Active)
        {
            return Results.Json(new AccessCodeFailure("member_blocked"), statusCode: 409);
        }

        var expiresAt = timeProvider.GetUtcNow() + MemberAccessCode.Validity;

        for (var attempt = 1; attempt <= CodeAttempts; attempt++)
        {
            var code = MemberAccessCode.Generate();

            member.AccessCode = code;
            member.AccessCodeExpiresAt = expiresAt;
            member.ConcurrencyStamp = Guid.NewGuid().ToString();

            // TrySaveAsync, not TrySaveChangesAsync: this write is guarded by a unique index
            // (IX_Members_AccessCode) as well as a concurrency token, and the two mean different
            // things here. A unique violation is a collision worth retrying with a new code; a
            // concurrency conflict means somebody else changed this member and our view is stale.
            var outcome = await unitOfWork.TrySaveAsync(cancellationToken);

            if (outcome == SaveOutcome.Saved)
            {
                return Results.Ok(new AccessCodeView(MemberAccessCode.Format(code), expiresAt));
            }

            if (outcome == SaveOutcome.ConcurrencyConflict)
            {
                return Results.Json(new AccessCodeFailure("conflict"), statusCode: 409);
            }

            // A unique violation. Discard so the retry re-reads rather than re-sending the rejected
            // value against a now-stale token — the same reason the booking loop discards.
            unitOfWork.DiscardChanges();

            member = await members.FindAsync(memberId, cancellationToken);
            if (member is null)
            {
                return Results.NotFound();
            }
        }

        return Results.Json(new AccessCodeFailure("conflict"), statusCode: 409);
    }
}
