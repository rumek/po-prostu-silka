using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain.Members;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// Books roster members into classes and collects what was refused (S-37).
///
/// <para>
/// ONE PROTOCOL TRANSACTION PER (MEMBER, CLASS). Never several inserts in one save: the class and pass
/// stamps make each booking its own optimistic lock, and a batch that stops halfway must leave every
/// booking it did make valid. A refusal is reported and the batch goes on - the way duplication
/// reports a clashing week rather than failing the rest.
/// </para>
///
/// <para>
/// EVERY CALLER COMMITS ITS OWN WRITE FIRST. A lost race inside the protocol discards the whole tracked
/// graph, so a roster row, class or karnet staged but not saved would vanish with it - and an entity
/// the caller loaded before the batch is detached afterwards, so responses are built from values.
/// </para>
///
/// <para>
/// A class instance rather than a static like <see cref="BookingProtocol"/>, because four handlers in
/// two bounded contexts call it and six dependencies per call site would bury what each one does.
/// It holds no state between calls.
/// </para>
/// </summary>
public sealed class RosterBooking(
    IMemberStore members,
    IClassStore classes,
    IBookingStore bookings,
    IMembershipPassStore passes,
    IGroupRosterStore roster,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    /// <summary>
    /// The reason a trainer's roster action gives for a class somebody else instructs. Not a protocol
    /// reason - the protocol knows nothing about who is asking - but read through the same SPA table.
    /// </summary>
    public const string NotYourClass = "not_your_class";

    /// <summary>
    /// Books every member into every class, classes in start order per member, so an exhausted karnet
    /// refuses the LATER classes.
    ///
    /// <para>
    /// <paramref name="actingInstructorId"/> is null for an admin and for every automatic trigger; a
    /// trainer's member id on the roster routes. When set, a class the trainer does not instruct is not
    /// attempted and is reported <see cref="NotYourClass"/>: S-16's "a trainer books into the classes
    /// they personally instruct" holds for roster bookings too.
    /// </para>
    /// </summary>
    public async Task<RosterReport> BookAsync(
        IReadOnlyList<Guid> memberIds,
        IReadOnlyList<RosterClass> targets,
        Guid? actingInstructorId,
        CancellationToken cancellationToken)
    {
        if (memberIds.Count == 0 || targets.Count == 0)
        {
            return RosterReport.Empty;
        }

        var ordered = targets.OrderBy(c => c.StartsAt).ToList();
        var booked = 0;
        var skipped = new List<RosterSkip>();

        foreach (var memberId in memberIds)
        {
            // Read into VALUES before any booking: a lost race below detaches this entity.
            var member = await members.FindAsync(memberId, cancellationToken);
            if (member is null)
            {
                // Gone between the roster read and now. Nothing to report about somebody who no longer exists.
                continue;
            }

            var name = member.DisplayName;

            // The two pre-checks BookForMember makes OUTSIDE the protocol, repeated here per member for
            // the same reason: the protocol stays a pure capacity-and-karnet transaction.
            var memberRefusal = member.Status != MembershipStatus.Active
                ? "member_blocked"
                : await members.IsStaffAsync(memberId, cancellationToken) ? "member_is_staff" : null;

            foreach (var target in ordered)
            {
                if (memberRefusal is not null)
                {
                    skipped.Add(new RosterSkip(memberId, name, target.Id, target.StartsAt, memberRefusal));
                    continue;
                }

                if (actingInstructorId is not null && target.InstructorMemberId != actingInstructorId)
                {
                    skipped.Add(new RosterSkip(memberId, name, target.Id, target.StartsAt, NotYourClass));
                    continue;
                }

                var attempt = await BookingProtocol.TryBookCoreAsync(
                    target.Id, memberId, classes, bookings, passes, unitOfWork, timeProvider, cancellationToken);

                switch (attempt)
                {
                    case BookingAttempt.Booked:
                        booked++;
                        break;

                    // The idempotent case "Uzupełnij zapisy" relies on - never a skip.
                    case BookingAttempt.Refused { Reason: "already_booked" }:
                        break;

                    case BookingAttempt.Refused refused:
                        skipped.Add(new RosterSkip(memberId, name, target.Id, target.StartsAt, refused.Reason));
                        break;

                    // Deleted since it was selected; there is no class left to report a gap on.
                    case BookingAttempt.ClassNotFound:
                        break;
                }
            }
        }

        return new RosterReport(booked, skipped);
    }

    /// <summary>The group's whole roster into the given classes - the create and duplicate hooks.</summary>
    public async Task<RosterReport> BookRosterIntoAsync(
        Guid groupId,
        IReadOnlyList<RosterClass> targets,
        CancellationToken cancellationToken)
    {
        if (targets.Count == 0)
        {
            return RosterReport.Empty;
        }

        var memberIds = await roster.MemberIdsAsync(groupId, cancellationToken);
        return await BookAsync(memberIds, targets, actingInstructorId: null, cancellationToken);
    }
}
