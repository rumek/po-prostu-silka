using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// The single construction of <see cref="ScheduledClass"/> from a tracked occurrence.
///
/// <para>
/// LIVES IN ITS OWN FILE SINCE S-18. It was internal-on-ClassEndpoints because BookingEndpoints
/// answers with the class as it now stands; once the endpoint classes move to Api and the
/// handlers stay here, a shared projection cannot belong to either one of them.
/// </para>
/// </summary>
internal static class ClassDtoMapping
{
    /// <summary>
    /// Projects an occurrence onto the wire contract.
    ///
    /// <para>
    /// THE RESOLVED PARTS ARE PASSED IN, not read off the entity's navigations. The name,
    /// description and instructor name do not live on the occurrence (prd-v2 FR-007, FR-009,
    /// FR-010), and the caller is not always holding an entity whose navigations are populated: a
    /// freshly created one has none, and an edited one still points at the PREVIOUS instructor.
    /// Taking them as parameters makes the caller state where each came from.
    /// </para>
    ///
    /// <para>
    /// The instructor arrives as a NAME rather than as an entity (S-14 Phase 8). The callers no
    /// longer hold the same type: the write paths have just validated an account, while the booking
    /// paths hold a tracked occurrence whose instructor is a Member. A string is the only thing this
    /// projection ever wanted from either.
    /// </para>
    /// </summary>
    /// <remarks>
    /// INTERNAL rather than private since S-08: BookingEndpoints answers with the class as it now
    /// stands, and two constructions of the same contract would drift the moment one of them learned
    /// about free spots and the other did not.
    /// </remarks>
    /// <param name="bookedCount">
    /// How many active bookings the occurrence has, which the caller must supply because this method
    /// has no query of its own — and must NOT reach through a navigation, because Class deliberately
    /// has no Bookings collection (see Booking.Class). A caller that has just created the occurrence
    /// passes 0; every other caller counts.
    /// </param>
    internal static ScheduledClass ToDto(
        Class entity, ClassType classType, string instructorName, int bookedCount) =>
        new(entity.Id,
            entity.ClassTypeId,
            classType.Name,
            classType.Description,
            entity.StartsAt,
            entity.DurationMinutes,
            entity.InstructorMemberId,
            instructorName,
            entity.Capacity,
            // Same construction as the read query, and unclamped for the same reason - see
            // ClassScheduleQuery.
            entity.Capacity - bookedCount,
            entity.Status.ToString());
}
