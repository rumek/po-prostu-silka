using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Training;

namespace po_prostu_silka.Application.Training;

/// <summary>
/// One plan in full, with its prescribed items (FR-017).
/// </summary>
public static class GetTrainingPlan
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        ITrainingPlanQuery query,
        CancellationToken cancellationToken)
    {
        var found = await query.FindDetailAsync(id, cancellationToken);

        return found is null ? Results.NotFound() : Results.Ok(found);
    }
}
