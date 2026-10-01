using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// Every group, for the admin list and the occurrence form (FR-004).
/// </summary>
public static class GetClassGroups
{
    /// <summary>
    /// Every group, active and inactive, active first and then by name.
    ///
    /// UNFILTERED, deliberately. The screen's "pokaż nieaktywne" toggle filters what it already
    /// holds; a server-side flag would make every flick of that toggle a round trip. A single club's
    /// group list is a handful of rows, so there is nothing to page.
    /// </summary>
    public static async Task<IResult> HandleAsync(
        IClassGroupQuery query,
        CancellationToken cancellationToken) =>
        Results.Ok(await query.GetAllAsync(cancellationToken));
}
