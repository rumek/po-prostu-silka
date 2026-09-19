using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// Who is booked into one occurrence, for the staff roster.
/// </summary>
public static class GetClassBookings
{
    /// <summary>
    /// Who signed up for a class (prd.md FR-014).
    ///
    /// <para>
    /// Active only. The admin is looking at who to expect, not at who changed their mind — the
    /// cancelled rows are history the application keeps but does not put in front of anyone.
    /// </para>
    /// </summary>
    public static async Task<IResult> HandleAsync(
        Guid classId,
        ClaimsPrincipal principal,
        IClassStore classes,
        IBookingQuery query,
        CancellationToken cancellationToken)
    {
        // The class is loaded for the ownership check and for nothing else. That is one extra read on
        // a read-only route, which is the price of the narrowing - and it is a keyed lookup.
        var entity = await classes.FindAsync(classId, cancellationToken);
        if (entity is null)
        {
            return Results.NotFound();
        }

        if (!BookingAuthorization.MayActOn(principal, entity))
        {
            return BookingAuthorization.NotYourClass();
        }

        return Results.Ok(await query.GetForClassAsync(classId, cancellationToken));
    }
}
