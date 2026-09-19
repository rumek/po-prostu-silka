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
/// The single construction of <see cref="CurrentUser"/> from an account plus its member record.
///
/// <para>
/// LIVES IN ITS OWN FILE SINCE S-18, for the same reason as ClassDtoMapping: ProfileEndpoints
/// answers with the same shape after an edit, and two constructions of one contract drift.
/// </para>
/// </summary>
internal static class CurrentUserBuilder
{
    /// <summary>
    /// Builds the session payload from an entity. Internal rather than private: ProfileEndpoints
    /// returns the same shape after a save, and a second copy of this projection is exactly how the
    /// two would drift the next time CurrentUser grows a field.
    ///
    /// <para>
    /// <c>MemberId</c> and <c>MembershipStatus</c> are NULLABLE on the wire and must stay that way.
    /// They are null exactly when the account has no member row — the state the claims factory treats
    /// as a tripwire — and the SPA reads that as "signed in but unusable" rather than inventing a
    /// status. Filling them in with a default here would hide the same failure the policies exist to
    /// surface.
    /// </para>
    /// </summary>
    internal static async Task<CurrentUser> BuildCurrentUserAsync(
        ApplicationUser user,
        UserManager<ApplicationUser> userManager,
        IMemberStore members)
    {
        var roles = await userManager.GetRolesAsync(user);
        var member = await members.FindByUserIdAsync(user.Id, CancellationToken.None);

        return new CurrentUser(
            user.Id,
            user.Email ?? string.Empty,
            user.DisplayName,
            user.Status.ToString(),
            [.. roles],

            // From the MEMBER since S-14 Phase 8 — see GetCurrentUser for why.
            member?.PhoneNumber,
            member?.Street,
            member?.HouseNumber,
            member?.PostalCode,
            member?.City,
            member?.Id,
            member?.Status.ToString());
    }
}
