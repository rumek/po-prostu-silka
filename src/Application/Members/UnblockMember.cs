using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Application.Scheduling;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// Lifts a block.
/// </summary>
public static class UnblockMember
{
    /// <summary>
    /// Unblocks a member (FR-004) — return them to the club, and to their login if they have one.
    ///
    /// Deliberately does NOT rotate the security stamp. Rotation exists to destroy a session carrying
    /// a stale PERMISSIVE claim; a blocked member has no such session, because block already rotated
    /// their stamp and their claim is refused either way. There is nothing to revoke, so revoking
    /// would only sign out a member we just let back in.
    ///
    /// No email either. There was never a notification for this and S-16 removed the only one that
    /// was adjacent to it (the approval welcome, which went with the approval flow itself). Telling
    /// somebody they have been unblocked would also mean telling them they had been blocked, which is
    /// a conversation the club has in person rather than by machine.
    /// </summary>
    public static async Task<IResult> HandleAsync(
        Guid memberId,
        IMemberStore members,
        UserManager<ApplicationUser> userManager,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var member = await members.FindAsync(memberId, cancellationToken);
        if (member is null)
        {
            return Results.NotFound();
        }

        // Idempotent, mirroring BlockAsync: a double-click must not be an error. Before S-14 this
        // branch could also mean "the account is Pending, so the action wanted is approve" and
        // answered 409 not_blocked. Membership has only two states, so that case no longer exists —
        // a member whose LOGIN is pending has an active membership and nothing to unblock, and
        // saying so with an error would be a lie about a no-op.
        if (member.Status == MembershipStatus.Active)
        {
            return Results.Ok();
        }

        member.Status = MembershipStatus.Active;
        member.ConcurrencyStamp = Guid.NewGuid().ToString();

        if (member.UserId is not null)
        {
            var user = await userManager.FindByIdAsync(member.UserId);
            if (user is not null && user.Status == AccountStatus.Blocked)
            {
                // Always to Active, never back to Pending. A member blocked while still Pending is
                // approved by this action - accepted deliberately (S-02 planning) so that no
                // prior-status column has to exist. The members screen says so on the button.
                user.Status = AccountStatus.Active;
                user.ConcurrencyStamp = Guid.NewGuid().ToString();
            }
        }

        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            return Results.Json(new UnblockFailure("conflict"), statusCode: 409);
        }

        return Results.Ok();
    }
}
