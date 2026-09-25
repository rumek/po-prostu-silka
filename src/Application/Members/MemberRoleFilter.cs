namespace po_prostu_silka.Application.Members;

/// <summary>
/// The admin list's second filter: which PERSONA a row is (S-25), not which roles it holds. The
/// positions partition the list with the SPA's precedence — Admin &gt; Trainer &gt; Member — so an
/// Admin+Trainer account is found under <see cref="Admin"/> only, and every row lands in exactly one
/// position. A member without an account holds no role at all and is a <see cref="Member"/>: they
/// train here like anyone else.
///
/// <para>
/// Bound as a nullable enum for the same reason <see cref="MemberListFilter"/> is: an unparseable
/// value is a 400 from binding rather than a silent "no filter". The values are pinned.
/// </para>
/// </summary>
public enum MemberRoleFilter
{
    /// <summary>Holds neither staff role — including a record with no login.</summary>
    Member = 1,

    /// <summary>Holds Trainer and not Admin.</summary>
    Trainer = 2,

    /// <summary>Holds Admin, whatever else.</summary>
    Admin = 3,
}
