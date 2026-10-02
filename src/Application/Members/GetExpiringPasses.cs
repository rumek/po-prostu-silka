namespace po_prostu_silka.Application.Members;

/// <summary>
/// The admin's Start card: members whose karnet is ending, nearest end first (expiring-passes-dashboard).
///
/// <para>
/// ITS OWN ROUTE, NOT <c>GetMembers?expiring=true&amp;pageSize=5</c>. The list is alphabetical with an id
/// tiebreak — its paging contract — and the card is ordered by urgency. Re-sorting the list under one
/// filter would give paging two orders to keep stable; a second read on the same predicate costs
/// nothing.
/// </para>
/// </summary>
public static class GetExpiringPasses
{
    /// <summary>How many rows the card shows before "Zobacz wszystkich (N)" takes over.</summary>
    public const int CardSize = 5;

    public static async Task<IResult> HandleAsync(IMemberQuery query, CancellationToken cancellationToken) =>
        Results.Ok(await query.GetExpiringPassesAsync(CardSize, cancellationToken));
}
