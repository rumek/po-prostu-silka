namespace po_prostu_silka.Application.Members;

/// <summary>
/// The bounds a karnet must satisfy, named once (S-16).
///
/// <para>
/// WHY THIS TYPE EXISTS. The same four limits are asserted in three places — the entity configuration
/// that sizes the column, the endpoint that refuses bad input, and the Angular form that stops the
/// admin before the round trip. Written three times, they drift, and the drift shows up as a form that
/// accepts a name the database truncates. One source, three readers, no second opinion.
/// </para>
///
/// <para>
/// Placed in Application beside <see cref="ContactDetails"/> and <see cref="MemberAccessCode"/>, for
/// the reason those two record: the refusals these bounds produce are the API's wire vocabulary, which
/// is a contract of the HTTP surface rather than a rule about a member. Pure BCL, so it needs no
/// database to be true.
/// </para>
/// </summary>
public static class MembershipPassRules
{
    /// <summary>
    /// Column width for the pass's type name. Mirrored by MembershipPassConfiguration — keep the two
    /// in step. 100 matches <c>Member.DisplayName</c>: both are short human labels typed at the desk.
    /// </summary>
    public const int TypeNameMaxLength = 100;

    /// <summary>
    /// One. THERE IS NO ZERO-ENTRY PASS — a pass that entitles its holder to nothing is not a pass,
    /// it is a data-entry mistake, and accepting it would put a member in a state where every booking
    /// is refused for a reason the admin cannot see on the screen.
    /// </summary>
    public const int MinEntryCount = 1;

    /// <summary>
    /// A sanity ceiling, not a product rule. It exists so a mistyped "1000" instead of "10" is caught
    /// at the boundary rather than becoming a pass nobody can exhaust. Raise it freely if the club
    /// ever sells something larger.
    /// </summary>
    public const int MaxEntryCount = 500;

    /// <summary>
    /// The widest validity span the endpoint accepts, in days, counting both ends.
    ///
    /// <para>
    /// Same purpose as <see cref="MaxEntryCount"/>: a year and a bit is longer than any karnet the
    /// club issues, so anything beyond it is a slipped digit in the year field — which would otherwise
    /// create a pass that blocks every future pass for that member on the non-overlap rule, with no
    /// obvious cause.
    /// </para>
    /// </summary>
    public const int MaxValidityDays = 400;
}
