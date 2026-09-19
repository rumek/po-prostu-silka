using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// The write counterpart. Intention-revealing methods rather than a generic repository — this
/// codebase has no repository pattern and this slice does not introduce one.
///
/// Nothing here saves. The endpoint commits through <see cref="IUnitOfWork"/>, which is what lets a
/// whole duplicate batch land in one transaction.
/// </summary>
public interface IClassStore
{
    /// <summary>
    /// One occurrence WITH its ClassType and Instructor navigations loaded — ToDto resolves the name,
    /// description and display name through them, so a bare entity is not enough.
    /// </summary>
    Task<Class?> FindAsync(Guid id, CancellationToken cancellationToken);

    void Add(Class entity);

    void Remove(Class entity);

    /// <summary>
    /// Whether another class already occupies any part of
    /// [startsAt, startsAt + durationMinutes) — ANYWHERE in the club (prd-v2 FR-012).
    ///
    /// <para>
    /// This was <c>HasRoomConflictAsync</c> until S-06. The room disappeared, but the rule did not:
    /// it widened from "one room, one class at a time" to "one club, one class at a time". A
    /// single-room gym could never have two classes at once anyway, so removing the room made the
    /// real rule explicit rather than removing the protection.
    /// </para>
    /// </summary>
    /// <param name="excludingId">
    /// The class being edited, so it does not conflict with itself. Null when creating.
    /// </param>
    Task<bool> HasTimeConflictAsync(
        DateTimeOffset startsAt,
        int durationMinutes,
        Guid? excludingId,
        CancellationToken cancellationToken);
}
