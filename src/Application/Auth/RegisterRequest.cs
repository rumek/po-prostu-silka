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
/// Registration input. THREE FIELDS, and that is the whole slice (S-17, IR-04): an address to sign
/// in with, a password, and the invitation code that says which member record this account attaches
/// to.
///
/// <para>
/// The display name, the phone number and the four address fields used to arrive here and no longer
/// do. They come from the <see cref="Member"/> the code claims — the club entered that person into
/// its records before handing the code over, so asking them to type it again would only produce a
/// second, competing copy. What the club does not hold, the member fills in later through
/// <c>PUT /api/profile</c>, which is where those five fields stay required.
/// </para>
/// </summary>
/// <param name="MemberCode">
/// REQUIRED since S-17 (IR-05). Registration is claim-only: there is no branch that creates a fresh
/// member record any more, so a request without a code cannot produce an account at all. Not
/// defaulted, so the compiler refuses a caller that forgets it.
/// </param>
public record RegisterRequest(
    string Email,
    string Password,
    string MemberCode);
