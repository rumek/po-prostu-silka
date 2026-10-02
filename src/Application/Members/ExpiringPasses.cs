namespace po_prostu_silka.Application.Members;

/// <summary>
/// The admin's Start card "Kończą się karnety" (expiring-passes-dashboard): the nearest few ends and
/// how many there are in all.
///
/// This is a CONTRACT the SPA's member-admin service mirrors — renaming a field breaks the card silently.
///
/// <para>
/// <see cref="Total"/> IS THE MEMBER LIST'S TOTAL under <c>expiring=true</c>, by construction: both
/// read one predicate. The card's "Zobacz wszystkich (N)" promises exactly that.
/// </para>
/// </summary>
public record ExpiringPasses(IReadOnlyList<ExpiringPass> Items, int Total);

/// <summary>
/// One member whose karnet is ending.
///
/// <para>
/// <see cref="DaysLeft"/> COMES FROM THE SERVER on purpose: it is computed from the same club-local
/// today the predicate used, so around midnight the card's "dziś" cannot disagree with the list a
/// browser in another clock would compute. 0 is today, the karnet's last valid day.
/// </para>
/// </summary>
public record ExpiringPass(Guid MemberId, string DisplayName, DateOnly ValidTo, int DaysLeft);
