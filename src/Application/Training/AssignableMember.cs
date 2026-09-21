using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Training;

namespace po_prostu_silka.Application.Training;

/// <summary>
/// The trainer-safe identity of a member. Deliberately three fields: a label, an id, and whether the
/// member can sign in — nothing on a trainer surface should expose e-mails or account status to an
/// account that is not an admin.
///
/// <para>
/// NOT ONLY AN ASSIGNABLE MEMBER any more, despite the name. It began as a picker row, which by
/// construction offered only members a plan may be given to; since S-22 it is also the member half of
/// <see cref="MemberPlan"/>, which an admin may read for a BLOCKED member. The name stayed because the
/// wire shape did.
/// </para>
/// </summary>
/// <param name="Id">The MEMBER's id (S-14), not an account's — which is what lets a person the club
/// recorded but who never registered be offered here at all.</param>
/// <param name="HasAccount">
/// Whether they can sign in. The picker says so, because a plan assigned to someone with no login is
/// real work the member will never see in the app until they claim their record.
/// </param>
public record AssignableMember(Guid Id, string DisplayName, bool HasAccount);
