using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Training;

namespace po_prostu_silka.Application.Training;

/// <summary>
/// Every training plan, for the trainer list (FR-015).
/// </summary>
public static class GetTrainingPlans
{
    /// <summary>
    /// Every ACTIVE plan in the club, by member name.
    ///
    /// <para>
    /// Archived plans are excluded and there is no way to ask for them: prd.md:164 cuts plan history
    /// from the MVP, so a screen that could show them does not exist. Unpaginated, for the same
    /// reason as every other admin list here - a single gym has as many active plans as it has
    /// members who train.
    /// </para>
    /// </summary>
    public static async Task<IResult> HandleAsync(
        ITrainingPlanQuery query,
        CancellationToken cancellationToken) =>
        Results.Ok(await query.GetActiveAsync(cancellationToken));
}
