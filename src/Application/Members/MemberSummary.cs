using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Application.Scheduling;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// A member as the admin's full list sees them (FR-005, and S-14's accountless records).
///
/// This is a CONTRACT the SPA's member-admin service mirrors — renaming a field breaks the members
/// screen silently.
///
/// <para>
/// TWO STATUSES, AND THEY ARE NOT THE SAME QUESTION. <see cref="MembershipStatus"/> is always present
/// and says whether the person may use the club; <see cref="AccountStatus"/> is null exactly when
/// <see cref="UserId"/> is null and says whether their login works. Collapsing them into one field was
/// the obvious simplification and it is wrong: an accountless member has no account status to report,
/// and reporting "Active" for them would claim a login exists.
/// </para>
///
/// <para>
/// Both cross the wire as enum NAMES, never their ints — the numeric values exist for persistence
/// stability and a badge keyed on 2 would break the day someone renumbers.
/// <see cref="Roles"/> follows the same rule and is empty for a member with no account, because roles
/// live in Identity and a record without a login holds none.
/// </para>
/// </summary>
public record MemberSummary(
    Guid Id,
    string? UserId,
    string DisplayName,
    string? Email,
    string MembershipStatus,
    string? AccountStatus,
    IReadOnlyList<string> Roles,
    bool HasAccessCode,
    DateTimeOffset CreatedAt);
