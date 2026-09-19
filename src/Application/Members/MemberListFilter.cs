using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Application.Scheduling;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// The positions the admin's filter offers. Bound as a nullable enum, so an unparseable value is a 400
/// from the framework's binding rather than a silent fall-through to "no filter" — the same reasoning
/// the account-status filter used before S-14 widened it.
///
/// <para>
/// These are the states an ADMIN thinks in, not a projection of either enum. That is why they overlap
/// the two underlying statuses rather than mirroring one: "pending" is a fact about a login, "without
/// account" is the absence of one, and "active" has to mean the same thing for a person with a login
/// and a person without.
/// </para>
/// </summary>
public enum MemberListFilter
{
    /// <summary>
    /// RETIRED (S-16, MP-03). This position selected accounts awaiting approval, and nothing produces
    /// that state any more. The NUMERIC VALUE STAYS RESERVED for the same reason
    /// <see cref="AccountStatus.Pending"/>'s does — the values are pinned, and re-using 0 for
    /// something else would silently repoint any stored or bookmarked filter.
    /// </summary>
    [Obsolete("Approval was retired in S-16; nothing produces Pending accounts. Value reserved.")]
    Pending = 0,

    /// <summary>May use the club: active membership, and an active account if there is one at all.</summary>
    Active = 1,

    /// <summary>Barred. Since S-14 a block sets both statuses together, so membership alone answers this.</summary>
    Blocked = 2,

    /// <summary>Recorded by the club, never registered. The case S-14 exists for.</summary>
    WithoutAccount = 3,
}
