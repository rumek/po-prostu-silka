using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// One occurrence in full, with its booked count.
/// </summary>
public static class GetClass
{
    /// <summary>
    /// One class, for the edit form.
    ///
    /// Exists so opening /admin/classes/:id directly - a bookmark, a refresh, a shared link - costs
    /// one row instead of the whole admin list. That list is deliberately unbounded, so filtering it
    /// client-side to find a single class would grow with every class the club ever schedules.
    /// </summary>
    public static async Task<IResult> HandleAsync(
        Guid id,
        IClassStore store,
        IBookingStore bookings,
        CancellationToken cancellationToken)
    {
        var found = await store.FindAsync(id, cancellationToken);

        // The one caller whose entity genuinely arrives with both navigations populated - FindAsync
        // Includes them - and the one place a null truly means "no such class".
        return found is null
            ? Results.NotFound()
            : Results.Ok(ClassDtoMapping.ToDto(
                found,
                found.ClassType,
                found.Instructor!.DisplayName,
                await bookings.CountActiveAsync(id, cancellationToken)));
    }
}
