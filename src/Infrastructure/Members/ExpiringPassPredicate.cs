using System.Linq.Expressions;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Infrastructure.Members;

/// <summary>
/// The one Infrastructure definition of "this member's karnet is ending" (expiring-passes-dashboard).
/// The admin's Start card and the member list's <c>expiring</c> filter both read it, so the number on
/// the card and the total of the list it links to cannot disagree.
///
/// <para>
/// THE CLUB'S SPREADSHEET RULE, PLUS WHAT THE SPREADSHEET CANNOT KNOW. "Klienci" marks a row
/// "KOŃCZY SIĘ" when its end date is between today and today + 5, both inclusive. That window is kept
/// exactly, so the app and the sheet agree about every person during the changeover. The sheet holds
/// one row per client and cannot see a renewal issued in advance; the app can, so a member holding
/// ANY karnet that starts after the current one ends is not ending — a gap between the two included.
/// Passes may not overlap, so "starts after it ends" is exactly "a later karnet".
/// </para>
///
/// <para>
/// THE END DATE, AND NOTHING ELSE ABOUT THE KARNET. Entries running low do not count: the signal is
/// about the money cycle, not usage. Payment does not count either — an unpaid karnet that is ending
/// is ending, and the debt is pass-paid-flag's filter, not this one (the sheet lets "BRAK PŁATNOŚCI"
/// mask "KOŃCZY SIĘ"; the app deliberately does not).
/// </para>
///
/// <para>
/// ONLY THE KARNET COVERING TODAY. An expired karnet is not ending (no "WYGASŁ" line), and neither is
/// one that has not started: there is nothing current to renew. Blocked members are excluded — the
/// card means "to renew", and a block means the club is not renewing. Staff are excluded through
/// <see cref="StaffPredicate"/>, for the reason <c>MemberQuery</c>'s unpaid filter gives: the pass
/// routes refuse a staff holder, so they are a renewal nobody could issue.
/// </para>
/// </summary>
public static class ExpiringPassPredicate
{
    /// <summary>How many days past today a karnet's last day may fall and still be "ending". Inclusive.</summary>
    public const int WindowDays = 5;

    public static Expression<Func<Member, bool>> IsExpiring(AppDbContext db, DateOnly today)
    {
        var horizon = today.AddDays(WindowDays);
        var isNotStaff = StaffPredicate.IsNotStaff(db);

        Expression<Func<Member, bool>> ending = member =>
            member.Status == MembershipStatus.Active
            && db.MembershipPasses.Any(current =>
                current.MemberId == member.Id
                && current.ValidFrom <= today
                && today <= current.ValidTo
                && current.ValidTo <= horizon
                && !db.MembershipPasses.Any(later =>
                    later.MemberId == member.Id && later.ValidFrom > current.ValidTo));

        // One parameter for both halves, so EF sees a single lambda rather than two to stitch.
        var body = new Rebind(isNotStaff.Parameters[0], ending.Parameters[0]).Visit(isNotStaff.Body);

        return Expression.Lambda<Func<Member, bool>>(Expression.AndAlso(ending.Body, body), ending.Parameters);
    }

    private sealed class Rebind(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == from ? to : base.VisitParameter(node);
    }
}
