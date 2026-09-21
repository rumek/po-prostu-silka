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
    /// A substring of the display name, and NOTHING ELSE — the trainer's search (S-22). A caller that
    /// also matches the e-mail does so on purpose, as <c>MemberQuery</c> does for the admin.
    /// </summary>
    public static IQueryable<Member> ByName(IQueryable<Member> members, string? term)
    {
        if (term is null)
        {
            return members;
        }

        var folded = Fold(term);

        return members.Where(m =>
            EF.Functions.Collate(m.DisplayName.Replace("ł", "l").Replace("Ł", "L"), Collation)
                .Contains(folded));
    }
}
