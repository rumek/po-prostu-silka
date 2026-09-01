namespace po_prostu_silka.Domain.Notifications;

/// <summary>
/// The delivery channels FR-021 requires. Email is the guaranteed channel the "no missed
/// cancellations" guardrail rests on; push is best-effort, because iOS only delivers it to a
/// home-screen-installed PWA on 16.4+ and some members will never subscribe.
///
/// Explicit numeric values, and do not reorder: this persists as an int, so renumbering would
/// silently reinterpret every existing row.
/// </summary>
public enum NotificationChannel
{
    Email = 0,
    Push = 1,
}
