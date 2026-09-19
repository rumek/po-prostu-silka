using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Application.Scheduling;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// Blocks a member and releases their future bookings (AM-002).
/// </summary>
public static class BlockMember
{
    /// <summary>
    /// Blocks a member (FR-004, and S-14's AM-002): bar them from the club, refuse them at login if
    /// they have one, and cut the session they may already hold.
    ///
    /// <para>
    /// SINCE S-14 THE BLOCK IS ON THE MEMBERSHIP, not the account, which is what lets an accountless
    /// record be blocked at all. When there IS an account the two move together and always have to:
    /// a person barred from the club whose login still worked would reach every screen the
    /// ActiveMember policy guards.
    /// </para>
    ///
    /// <para>
    /// It rotates the account's SECURITY stamp as well, which is what actually ends a live session.
    /// Without it, a blocked member keeps a valid cookie carrying account_status=Active and sails
    /// past the policy until it happens to be re-minted. Note this is assigned directly rather than
    /// via UserManager.UpdateSecurityStampAsync, which would issue its OWN save and split the block
    /// into two writes.
    /// </para>
    ///
    /// <para>
    /// No notification. The PRD asks for no block email, and telling someone they have been blocked is
    /// a product decision nobody has made.
    /// </para>
    /// </summary>
    public static async Task<IResult> HandleAsync(
        Guid memberId,
        IMemberStore members,
        UserManager<ApplicationUser> userManager,
        IBookingStore bookings,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var member = await members.FindAsync(memberId, cancellationToken);
        if (member is null)
        {
            return Results.NotFound();
        }

        ApplicationUser? user = null;
        if (member.UserId is not null)
        {
            user = await userManager.FindByIdAsync(member.UserId);

            // THE ONLY THING stopping the club from locking itself out of its own app. Do not remove
            // or weaken it. The screen must not offer block on an admin row — but the screen is not
            // the boundary; this is. Checked by ROLE, so it holds if a second admin is ever seeded.
            if (user is not null && await userManager.IsInRoleAsync(user, ApplicationRoles.Admin))
            {
                return Results.Json(new BlockFailure("is_admin"), statusCode: 409);
            }
        }

        // Idempotent, like approve: a double-click must not be an error.
        if (member.Status == MembershipStatus.Blocked)
        {
            return Results.Ok();
        }

        member.Status = MembershipStatus.Blocked;
        member.ConcurrencyStamp = Guid.NewGuid().ToString();

        // A LIVE CODE DIES WITH THE BLOCK. RegisterAsync already refuses a blocked member's code, so
        // nothing can be claimed around this - but the refusal is a read-time check, and the code
        // would come back the instant the member is unblocked. That is a code the admin issued
        // under circumstances the club has since revisited, quietly reactivated by an unrelated act.
        // Revoking here means unblocking restores membership and nothing else; issue a new one.
        member.AccessCode = null;
        member.AccessCodeExpiresAt = null;

        if (user is not null)
        {
            // Blockable from any status. This used to say "Active AND Pending", so that a junk
            // registration could be stopped without first approving it; since S-16 every account is
            // Active from the moment it exists, and stopping a junk one is the ordinary case rather
            // than the awkward one.
            user.Status = AccountStatus.Blocked;
            user.ConcurrencyStamp = Guid.NewGuid().ToString();
            user.SecurityStamp = Guid.NewGuid().ToString();
        }

        // THE ONE STORED CASCADE IN THIS APPLICATION, and a deliberate exception to the convention
        // this file otherwise follows: access consequences are enforced at READ time by policy
        // claims, never by rewriting stored state. The exception is product-driven - a blocked member
        // cannot attend, and leaving their seats held would have the schedule promise spots to
        // nobody while other members are turned away as full.
        //
        // Queued into the SAME unit of work as the status flips, so a member is never blocked with
        // their bookings still held, nor released while still Active.
        //
        // KEYED ON THE MEMBER, so it now covers someone the admin blocked who has no account at all —
        // which is the case the whole slice exists for, and the one this call could not reach while
        // bookings were keyed on the login.
        //
        // FUTURE ONLY. Past bookings are attendance history and rewriting them would falsify it.
        //
        // NO CLASS STAMP IS ROTATED, and none is owed: cancelling only ever FREES spots, so a booker
        // racing this cascade reads a count that is conservative rather than permissive.
        await bookings.CancelActiveFutureForMemberAsync(
            member.Id, timeProvider.GetUtcNow(), cancellationToken);

        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            // Unlike approve, a lost race here is NOT safe to report as success. The winner may have
            // approved this member rather than blocked them, which would leave us telling the admin
            // "blocked" about an account that is now Active. Say the view is stale and let the
            // screen refetch.
            return Results.Json(new BlockFailure("conflict"), statusCode: 409);
        }

        return Results.Ok();
    }
}
