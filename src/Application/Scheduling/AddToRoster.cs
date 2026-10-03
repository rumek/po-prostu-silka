using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// Adds a member to a group's fixed roster and books them into its upcoming classes (S-37).
/// </summary>
public static class AddToRoster
{
    /// <summary>
    /// Adds, commits, THEN books.
    ///
    /// <para>
    /// THE ROSTER ROW IS SAVED BEFORE ANY BOOKING. The booking protocol discards the whole tracked graph
    /// on a lost race, so an unsaved row would vanish with it. And the row stays even when no class
    /// could be booked: a member without a karnet today is still in the group, and their karnet - or
    /// "Uzupełnij zapisy" - books them later.
    /// </para>
    ///
    /// <para>
    /// THE CAP IS SOFT. The count is checked without a lock, so two simultaneous adds can pass it by one;
    /// the guarantee that matters - the class's capacity - is the protocol's, and holds regardless. A
    /// duplicate member IS prevented, by IX_GroupRosterEntries_Group_Member.
    /// </para>
    /// </summary>
    public static async Task<IResult> HandleAsync(
        Guid groupId,
        [FromBody] AddToRosterRequest request,
        ClaimsPrincipal principal,
        IClassGroupStore groups,
        IGroupRosterStore roster,
        IGroupRosterQuery query,
        IMemberStore members,
        RosterBooking rosterBooking,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var (failure, group) = await RosterAuthorization.AuthorizeAsync(
            principal, groupId, groups, roster, timeProvider, cancellationToken);
        if (failure is not null)
        {
            return failure;
        }

        var member = await members.FindAsync(request.MemberId, cancellationToken);
        if (member is null)
        {
            return Results.NotFound();
        }

        // A deactivated group takes no new members. Its existing roster still syncs, and the hooks still
        // fill its remaining classes - FR-006 keeps those occurrences intact.
        if (!group!.IsActive)
        {
            return RosterFailure.Refuse("inactive_class_group");
        }

        if (await roster.FindAsync(groupId, member.Id, cancellationToken) is not null)
        {
            return RosterFailure.Refuse("already_in_roster");
        }

        if (await roster.CountAsync(groupId, cancellationToken) >= group.DefaultCapacity)
        {
            return RosterFailure.Refuse("roster_full");
        }

        // Refused at the door rather than added and reported forever: a roster of people nobody may book
        // is a list the club would maintain for nothing. A member blocked or promoted LATER stays, and
        // their automatic bookings report it.
        if (member.Status != MembershipStatus.Active)
        {
            return RosterFailure.Refuse("member_blocked");
        }

        if (await members.IsStaffAsync(member.Id, cancellationToken))
        {
            return RosterFailure.Refuse("member_is_staff");
        }

        var now = timeProvider.GetUtcNow();
        var memberId = member.Id;

        roster.Add(new GroupRosterEntry
        {
            Id = Guid.NewGuid(),
            ClassGroupId = groupId,
            MemberId = memberId,
            AddedAt = now,
            AddedBy = principal.FindFirstValue(ClaimTypes.NameIdentifier),
        });

        var outcome = await unitOfWork.TrySaveAsync(cancellationToken);
        if (outcome != SaveOutcome.Saved)
        {
            // A unique violation is the same person added at the same instant by somebody else.
            return RosterFailure.Refuse(outcome == SaveOutcome.UniqueViolation ? "already_in_roster" : "conflict");
        }

        var acting = RosterAuthorization.ActingInstructorId(principal);

        var classes = await roster.UpcomingClassesAsync(groupId, now, cancellationToken);
        var report = await rosterBooking.BookAsync([memberId], classes, acting, cancellationToken);

        var view = await query.GetViewAsync(groupId, timeProvider.GetUtcNow(), acting, cancellationToken);
        return Results.Ok(new RosterChange(view!, report));
    }
}
