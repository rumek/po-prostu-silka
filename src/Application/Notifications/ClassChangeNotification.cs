using po_prostu_silka.Application.Scheduling;
using po_prostu_silka.Domain.Notifications;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Notifications;

/// <summary>
/// A class as a MESSAGE names it: the four things a member recognises it by.
///
/// <para>
/// PASSED IN RATHER THAN READ OFF THE ENTITY, for the reason ClassEndpoints.ToDto records. An
/// occurrence carries neither its type's name nor its instructor's display name (prd-v2 FR-007,
/// FR-009), and after a trainer reassignment the tracked entity's Instructor navigation still points
/// at the PREVIOUS account — so a service reaching through navigations would render a stale name in
/// exactly the message whose subject is that the trainer changed.
/// </para>
/// </summary>
public record ClassDescription(
    string Name,
    DateTimeOffset StartsAt,
    int DurationMinutes,
    string Instructor);

/// <summary>
/// FR-021's other half: what the members holding a spot are told when their class is cancelled
/// (FR-013) or moved (S-09).
///
/// <para>
/// THE SIBLING OF <see cref="IAccountApprovedNotification"/>, and deliberately the same shape:
/// rendered HERE rather than in the worker, one outbox row per recipient per channel, and NO SAVE.
/// The caller owns the unit of work, which is the whole point — ClassEndpoints commits the status
/// flip and these rows in one SaveChangesAsync, so "cancelled but nobody told" is unreachable rather
/// than merely unlikely.
/// </para>
///
/// <para>
/// ONE SERVICE FOR BOTH TRIGGERS. Recipient resolution and channel fan-out are identical; only the
/// rendered text differs, so splitting this in two would duplicate the part that can go wrong and
/// separate the part that cannot.
/// </para>
/// </summary>
public interface IClassChangeNotification
{
    /// <summary>
    /// Tells everyone in <paramref name="recipients"/> that the class is not happening.
    ///
    /// <para>
    /// An empty recipient list is legal and enqueues nothing: cancelling a class nobody signed up
    /// for is an ordinary admin action, not a special case the caller has to guard.
    /// </para>
    /// </summary>
    Task NotifyCancelledAsync(
        ClassDescription description,
        IReadOnlyList<ClassBooking> recipients,
        CancellationToken cancellationToken);

    /// <summary>
    /// Tells everyone in <paramref name="recipients"/> what moved, naming the old value and the new
    /// one for each field that actually changed.
    /// </summary>
    /// <param name="previous">
    /// The class as it stood BEFORE the edit. The caller must capture this before mutating the
    /// tracked entity — reading it afterwards yields the new values and the message renders
    /// "18:00 → 18:00".
    /// </param>
    Task NotifyChangedAsync(
        ClassDescription previous,
        ClassDescription current,
        IReadOnlyList<ClassBooking> recipients,
        CancellationToken cancellationToken);
}

public class ClassChangeNotification(
    IOutboxEnqueuer enqueuer,
    IPushSubscriptionStore subscriptions) : IClassChangeNotification
{
    /// <summary>Mirrors <c>OutboxMessageConfiguration</c>'s <c>HasMaxLength(200)</c> on Subject.</summary>
    private const int MaxSubjectLength = 200;

    public Task NotifyCancelledAsync(
        ClassDescription description,
        IReadOnlyList<ClassBooking> recipients,
        CancellationToken cancellationToken)
    {
        var subject = Subject("Odwołane zajęcia: ", description.Name);

        var body =
            $"Zajęcia {description.Name} zostały odwołane.\n\n" +
            $"Termin: {MessageTime.ToClubWallClock(description.StartsAt)}\n" +
            $"Prowadzący: {description.Instructor}\n\n" +
            "Twoja rezerwacja została anulowana. Zapraszamy na inne zajęcia w aplikacji.";

        return FanOutAsync(subject, body, recipients, cancellationToken);
    }

    public Task NotifyChangedAsync(
        ClassDescription previous,
        ClassDescription current,
        IReadOnlyList<ClassBooking> recipients,
        CancellationToken cancellationToken)
    {
        var subject = Subject("Zmiana w zajęciach: ", current.Name);

        // Only the fields that actually moved. A message listing three lines where one changed makes
        // the reader hunt for the difference, which is the opposite of what a notification is for.
        var changes = new List<string>();

        if (previous.StartsAt != current.StartsAt)
        {
            changes.Add(
                $"Termin: {MessageTime.ToClubWallClock(previous.StartsAt)} " +
                $"-> {MessageTime.ToClubWallClock(current.StartsAt)}");
        }

        if (previous.DurationMinutes != current.DurationMinutes)
        {
            changes.Add(
                $"Czas trwania: {previous.DurationMinutes} min -> {current.DurationMinutes} min");
        }

        if (previous.Instructor != current.Instructor)
        {
            changes.Add($"Prowadzący: {previous.Instructor} -> {current.Instructor}");
        }

        var body =
            $"Zmieniły się szczegóły zajęć {current.Name}.\n\n" +
            string.Join("\n", changes) +
            $"\n\nAktualny termin: {MessageTime.ToClubWallClock(current.StartsAt)}\n\n" +
            "Jeśli nowy termin Ci nie odpowiada, możesz anulować rezerwację w aplikacji.";

        return FanOutAsync(subject, body, recipients, cancellationToken);
    }

    /// <summary>
    /// The outbox <c>Subject</c> column is nvarchar(200) and <c>ClassType.Name</c> is itself allowed
    /// the full 200 characters, so a prefixed subject can overflow it. That is not a cosmetic
    /// failure: SQL Server refuses the insert, the truncation error surfaces as a
    /// <c>DbUpdateException</c> which <c>TrySaveChangesAsync</c> does not catch, and because the
    /// enqueue shares its unit of work with the status flip, the cancellation itself would never
    /// commit. Trim the name, never the prefix — the prefix is what a member scans a mailbox for.
    /// </summary>
    private static string Subject(string prefix, string name)
    {
        var subject = prefix + name;

        return subject.Length <= MaxSubjectLength
            ? subject
            : string.Concat(subject.AsSpan(0, MaxSubjectLength - 1), "…");
    }

    /// <summary>
    /// One email row per recipient, plus one push row per device that recipient has registered —
    /// the fan-out <see cref="AccountApprovedNotification"/> established, widened from one member to
    /// a class full of them.
    ///
    /// <para>
    /// The subscription lookup is one round trip PER MEMBER rather than one for the whole list. For a
    /// club whose classes hold a dozen or two people that is a dozen or two indexed reads inside a
    /// request that is already writing that many rows; batching it would mean a second store method
    /// existing solely for this caller. Revisit if a class ever holds hundreds.
    /// </para>
    /// </summary>
    private async Task FanOutAsync(
        string subject,
        string body,
        IReadOnlyList<ClassBooking> recipients,
        CancellationToken cancellationToken)
    {
        foreach (var recipient in recipients)
        {
            // Email may be blank only in theory — registration is by email — but ClassBooking
            // coalesces a null away, so an empty string is what would arrive. Enqueuing a row with no
            // recipient would fail delivery repeatedly and burn the retry budget for nothing.
            if (!string.IsNullOrWhiteSpace(recipient.Email))
            {
                enqueuer.Enqueue(NotificationChannel.Email, recipient.Email, subject, body);
            }

            var devices = await subscriptions.GetForUserAsync(
                recipient.MemberUserId, cancellationToken);

            foreach (var device in devices)
            {
                // The subscription id, not the endpoint: the worker looks the row up so it can
                // delete it when the push service reports the subscription is gone.
                enqueuer.Enqueue(NotificationChannel.Push, device.Id.ToString(), subject, body);
            }
        }
    }
}
