using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// One class occurrence as the schedule and the admin list see it. This is a CONTRACT the SPA's class
/// service mirrors — renaming a field breaks both screens silently.
///
/// <para>
/// TWO OF THESE FIELDS ARE RESOLVED, NOT STORED (prd-v2 FR-007, FR-010). <see cref="Name"/> and
/// <see cref="Description"/> come from the occurrence's ClassType and <see cref="Instructor"/> from
/// the assigned account's display name — the occurrence itself holds none of the three. That is what
/// makes correcting a typo on the type correct it on every week at once, past occurrences included.
/// </para>
///
/// <para>
/// <see cref="Capacity"/> and <see cref="DurationMinutes"/> are the opposite: COPIES taken at
/// creation, owned by this occurrence, and never re-read from the type. The asymmetry is deliberate
/// and load-bearing — capacity resolved through the type would let a type edit move the value the
/// no-overbooking guarantee is checked against.
/// </para>
///
/// <para>
/// Status crosses the wire as the enum NAME ("Scheduled" / "Cancelled"), not its int, for the same
/// reason AccountStatus does: the numeric values exist for persistence stability, and a badge keyed
/// on 1 would break the day someone renumbers.
/// </para>
///
/// <para>
/// FreeSpots is <see cref="Capacity"/> until S-08 — see IClassScheduleQuery. There is no Room: the
/// club has one, so the field never carried information (prd-v2 FR-011).
/// </para>
///
/// <para>
/// <see cref="InstructorMemberId"/> REACHES MEMBERS, and that is a considered decision rather than an
/// oversight. ClassScheduleQuery projects one shape for both the admin list and the member schedule,
/// so every active member receives the trainer's member id. The member SPA never reads it — the field
/// exists for the admin form's trainer select — and it grants nothing on its own, since every admin
/// surface is policy-gated. Splitting the projection in two was weighed and declined: it would hand
/// S-07 and S-08 a branch to maintain for a field with no exploit path.
///
/// SINCE S-14 IT IS A MEMBER ID RATHER THAN AN IDENTITY ID, which narrows this further: it no longer
/// leaks a login identifier to every member of the club.
/// </para>
/// </summary>
public record ScheduledClass(
    Guid Id,
    Guid ClassTypeId,
    string Name,
    string? Description,
    DateTimeOffset StartsAt,
    int DurationMinutes,
    Guid InstructorMemberId,
    string Instructor,
    int Capacity,
    int FreeSpots,
    string Status);
