using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// One of the caller's own upcoming bookings, as "Moje zajęcia" shows it (prd.md FR-010).
///
/// <para>
/// A CONTRACT the SPA's booking service mirrors, like <see cref="ScheduledClass"/>. It is
/// deliberately NOT a ScheduledClass: that shape carries capacity, free spots and the instructor's
/// Identity id, none of which the list needs, and it lacks the two fields that make a row a booking
/// rather than a class — <see cref="BookingId"/> and <see cref="BookedAt"/>.
/// </para>
///
/// <para>
/// <see cref="Name"/>, <see cref="Description"/> and <see cref="Instructor"/> are RESOLVED through
/// the class's type and instructor, exactly as they are on the schedule — a booking stores none of
/// the three, so correcting a typo on a class type corrects it here too.
/// </para>
/// </summary>
public record MyBooking(
    Guid BookingId,
    Guid ClassId,
    string Name,
    string? Description,
    DateTimeOffset StartsAt,
    int DurationMinutes,
    string Instructor,
    DateTimeOffset BookedAt);
