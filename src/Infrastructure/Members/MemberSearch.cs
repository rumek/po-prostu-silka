using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using po_prostu_silka.Domain.Members;

namespace po_prostu_silka.Infrastructure.Members;

/// <summary>
/// What "matches" means when someone types part of a member's name (S-21, S-22). One definition,
/// shared by the admin's member list and the trainer's, so the two searches can never disagree on
/// whether "lukasz" finds "Łukasz".
///
/// <para>
/// <c>ł</c> IS FOLDED BY HAND, on both sides. Every other Polish diacritic decomposes into a base
/// letter plus a combining mark, which is what an accent-insensitive collation ignores; <c>ł</c>
/// is a letter of its own with no decomposition, so under any AI collation "lukasz" still does
/// not find "Łukasz". An admin on a phone rarely types Polish letters, and Ł starts common names.
/// </para>
///
/// <para>
/// A <c>%</c> or <c>_</c> in the term matches literally: EF translates <c>Contains</c> over a
/// parameter with the wildcards escaped, and <c>MemberAdminEndpointTests</c> pins that.
/// </para>
/// </summary>
internal static class MemberSearch
{
    /// <summary>
    /// The collation a search compares under: case- AND accent-insensitive, so "gesl" finds
    /// "Gęślicka" and "ZANETA" finds "Żaneta". The 100 series, because the older Latin1_General
    /// tables predate several of the weights this depends on.
    /// </summary>
    public const string Collation = "Latin1_General_100_CI_AI";

    /// <summary>The client-side half of the <c>ł</c> fold, applied to the typed term.</summary>
    public static string Fold(string value) => value.Replace('ł', 'l').Replace('Ł', 'L');

    /// <summary>
    /// The ONE column-side match: the column folded, compared under <see cref="Collation"/>, containing
    /// the already-folded term. Written once and inlined into each predicate below, because EF cannot
    /// translate a call to a helper method — only the expression the helper would have returned.
    /// </summary>
    private static readonly Expression<Func<string, string, bool>> Matches = (column, term) =>
        EF.Functions.Collate(column.Replace("ł", "l").Replace("Ł", "L"), Collation).Contains(term);

    /// <summary>
    /// A substring of the display name, and NOTHING ELSE — the trainer's search (S-22). A caller that
    /// also matches the e-mail does so on purpose, through <see cref="ByNameOrEmail"/>.
    /// </summary>
    public static IQueryable<Member> ByName(IQueryable<Member> members, string? term) =>
        term is null ? members : members.Where(Predicate(term, includeEmail: false));

    /// <summary>
    /// The display name OR the e-mail — the admin's search (S-21), and never a trainer's: a search that
    /// matched addresses would answer "does anyone's e-mail contain x" through its result count.
    /// </summary>
    public static IQueryable<Member> ByNameOrEmail(IQueryable<Member> members, string? term) =>
        term is null ? members : members.Where(Predicate(term, includeEmail: true));

    private static Expression<Func<Member, bool>> Predicate(string term, bool includeEmail)
    {
        // Captured through a closure, not a constant, so EF sends it as a SQL parameter.
        var folded = Fold(term);
        Expression<Func<string>> captured = () => folded;

        var member = Expression.Parameter(typeof(Member), "m");
        Expression body = Inline(Expression.Property(member, nameof(Member.DisplayName)), captured.Body);

        if (includeEmail)
        {
            var email = Expression.Property(member, nameof(Member.Email));
            body = Expression.OrElse(
                body,
                Expression.AndAlso(
                    Expression.NotEqual(email, Expression.Constant(null, typeof(string))),
                    Inline(email, captured.Body)));
        }

        return Expression.Lambda<Func<Member, bool>>(body, member);
    }

    private static Expression Inline(Expression column, Expression term) =>
        new Substitution(Matches.Parameters[0], column, Matches.Parameters[1], term).Visit(Matches.Body);

    private sealed class Substitution(
        ParameterExpression column,
        Expression columnValue,
        ParameterExpression term,
        Expression termValue) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == column ? columnValue : node == term ? termValue : base.VisitParameter(node);
    }
}
