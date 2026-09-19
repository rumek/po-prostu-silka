using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Training;

namespace po_prostu_silka.Application.Training;

/// <summary>
/// The members a plan may be assigned to (FR-016).
///
/// <para>
/// ITS ROUTE MUST STAY REGISTERED BEFORE /{id:guid}. The literal /members would otherwise be
/// a candidate for the id route; the :guid constraint masks that today, and the ordering is
/// what keeps it deterministic if the constraint ever loosens.
/// </para>
/// </summary>
public static class GetAssignableMembers
{
    /// <summary>
    /// The members a plan may be assigned to: approved accounts, by display name.
    ///
    /// <para>
    /// Exists because /api/admin/members is Admin-only and a trainer needs nothing from it but a name
    /// and an id. Loosening that endpoint instead would have handed every trainer the club's email
    /// list and account statuses to draw one dropdown.
    /// </para>
    /// </summary>
    public static async Task<IResult> HandleAsync(
        ITrainingPlanQuery query,
        CancellationToken cancellationToken) =>
        Results.Ok(await query.GetAssignableMembersAsync(cancellationToken));
}
