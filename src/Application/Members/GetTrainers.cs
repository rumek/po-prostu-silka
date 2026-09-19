using po_prostu_silka.Domain;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// Every ACTIVE account holding the Trainer role, by display name.
///
/// <para>
/// The active filter is not cosmetic: it is the read-side half of the rule
/// <c>ClassEndpoints</c> enforces on write. A blocked trainer must not be offered in the
/// selection, or the admin picks a name the server then refuses.
/// </para>
///
/// <para>
/// No pagination and no filter parameter — a single club's trainer list is a handful of rows, and
/// this endpoint exists to fill one dropdown.
/// </para>
/// </summary>
public static class GetTrainers
{
    public static async Task<IResult> HandleAsync(
        ITrainerQuery query,
        CancellationToken cancellationToken) =>
        Results.Ok(await query.GetActiveTrainersAsync(cancellationToken));
}
