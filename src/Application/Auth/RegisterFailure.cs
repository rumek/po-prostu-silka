using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;

namespace po_prostu_silka.Application.Auth;

/// <summary>
/// Why registration failed. Never echoes Identity's raw error text to the client.
///
/// <para>
/// S-14 adds two. <c>invalid_member_code</c> (400) is a format failure - what was typed could not be
/// a code at all. <c>unknown_member_code</c> (409) covers "no member holds it", "it expired" and "it
/// was revoked" as ONE answer, deliberately: distinguishing them would confirm to a stranger that a
/// code once existed, and the same reasoning already collapses ResetPasswordFailure's four causes
/// into <c>invalid_token</c>. S-17 makes <c>invalid_member_code</c> the answer to a MISSING code too,
/// which is the same thing now that registration is claim-only.
/// </para>
///
/// <para>
/// S-17 also RETIRES five. <c>invalid_display_name</c> and the <see cref="ContactDetails"/> codes -
/// <c>invalid_phone</c> / <c>invalid_street</c> / <c>invalid_house_number</c> /
/// <c>invalid_postal_code</c> / <c>invalid_city</c> - are unreachable from this endpoint, because the
/// fields behind them stopped arriving. They remain the vocabulary of <c>PUT /api/profile</c>, which
/// still requires all five; nothing here produces them.
/// </para>
/// </summary>
public record RegisterFailure(string Reason);
