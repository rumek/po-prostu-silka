using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Auth;
using po_prostu_silka.Domain;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// What a member may change about themselves (S-13). The five contact fields, and nothing else.
///
/// <para>
/// DISPLAY NAME AND EMAIL ARE ABSENT ON PURPOSE, and their absence from this record is the whole
/// enforcement. The gym owns the name on the membership, so a member sending one is not refused with
/// an error - the field simply is not part of the contract and never reaches the entity. Do not add
/// them here "for completeness": that would silently make FR-006's rewritten rule false again.
/// </para>
/// </summary>
public record ProfileRequest(
    string PhoneNumber,
    string Street,
    string HouseNumber,
    string PostalCode,
    string City);

/// <summary>
/// Why a profile save failed. Same five reason codes registration answers with, from the same
/// <see cref="ContactDetails"/> helper - one vocabulary the SPA maps onto controls once.
/// </summary>
public record ProfileFailure(string Reason);

/// <summary>
/// The member's own account surface.
///
/// <para>
/// Lives in <c>Members</c> rather than <c>Auth</c> because it is member data, not credentials or
/// session. The password endpoints that land with S-13's later phases stay in <c>Auth</c> for the
/// same reason, inverted.
/// </para>
/// </summary>
public static class ProfileEndpoints
{
    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/profile").WithTags("Profile");

        // Bare RequireAuthorization(), NEVER the ActiveMember policy - the same rule /me and
        // /refresh follow. An account registered before S-13 has no contact details, and the screen
        // that prompts it to supply them is reachable while still Pending. Gating this on approval
        // would make the prompt appear on a screen whose save button always 403s.
        group.MapPut("/", UpdateProfileAsync).RequireAuthorization();

        return app;
    }

    private static async Task<IResult> UpdateProfileAsync(
        [FromBody] ProfileRequest request,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager)
    {
        var user = await userManager.GetUserAsync(principal);

        // The cookie authenticated but the row is gone - a deleted account with a live cookie. Same
        // check, and the same answer, as GetCurrentUser.
        if (user is null)
        {
            return Results.Unauthorized();
        }

        if (!ContactDetails.TryCreate(
                request.PhoneNumber,
                request.Street,
                request.HouseNumber,
                request.PostalCode,
                request.City,
                out var contact,
                out var failure))
        {
            return Results.Json(new ProfileFailure(failure), statusCode: 400);
        }

        user.PhoneNumber = contact.PhoneNumber;
        user.Street = contact.Street;
        user.HouseNumber = contact.HouseNumber;
        user.PostalCode = contact.PostalCode;
        user.City = contact.City;

        var updated = await userManager.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            // Nothing here is user-correctable: the fields were already validated, so a failure at
            // this point is a concurrency stamp or a database problem. Do not map Identity's error
            // text onto a control - it would blame a field the member just fixed.
            return Results.Problem("Profile could not be saved.", statusCode: 500);
        }

        // The same shape /me returns, built by the same projection, so the SPA replaces its session
        // signal from this response instead of re-fetching.
        return Results.Ok(await AuthEndpoints.BuildCurrentUserAsync(user, userManager));
    }
}
