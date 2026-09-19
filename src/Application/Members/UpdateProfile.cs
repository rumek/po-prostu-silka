using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Auth;
using po_prostu_silka.Domain;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// Writes the member's own contact details.
/// </summary>
public static class UpdateProfile
{
    public static async Task<IResult> HandleAsync(
        [FromBody] ProfileRequest request,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager,
        ILoggerFactory loggerFactory,
        IMemberStore members)
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

        // THE ADDRESS LIVES ON THE MEMBER (S-14 Phase 8). The account's four columns are frozen and
        // go in the next release, so writing them here would only be maintaining a copy nothing
        // reads. PhoneNumber is the exception and stays in step: it is Identity's OWN column, it
        // survives the drop, and letting it drift from the number the member just typed would leave
        // a lie in the table Identity itself works from.
        user.PhoneNumber = contact.PhoneNumber;

        // Staged here and committed by the same UpdateAsync below: UserManager's save goes through
        // the SAME scoped DbContext, so the two land in one SaveChangesAsync rather than two writes
        // that can half-fail.
        var member = await members.FindByUserIdAsync(user.Id, CancellationToken.None);
        if (member is not null)
        {
            member.PhoneNumber = contact.PhoneNumber;
            member.Street = contact.Street;
            member.HouseNumber = contact.HouseNumber;
            member.PostalCode = contact.PostalCode;
            member.City = contact.City;
            member.ConcurrencyStamp = Guid.NewGuid().ToString();
        }

        var updated = await userManager.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            // Nothing here is user-correctable: the fields were already validated, so a failure at
            // this point is a concurrency stamp or a database problem. Do not map Identity's error
            // text onto a control - it would blame a field the member just fixed. Logged rather than
            // returned, like RegisterAsync's role-assignment failure, because "saving my address
            // 500s" is otherwise unreportable and undiagnosable.
            //
            // THE CATEGORY IS PINNED TO ProfileEndpoints, not to this class (S-18). The logger
            // category is a string that reaches Azure log queries, so it is observable behaviour;
            // letting it follow the handler into its new home would have broken those queries in the
            // middle of a refactor that promised to change nothing. Do not "tidy" this to
            // typeof(UpdateProfile).
            loggerFactory
                .CreateLogger("po_prostu_silka.Application.Members.ProfileEndpoints")
                .LogError(
                    "Profile update failed for user {UserId}. Errors: {Errors}",
                    user.Id,
                    string.Join("; ", updated.Errors.Select(e => e.Description)));

            return Results.Problem("Profile could not be saved.", statusCode: 500);
        }

        // The same shape /me returns, built by the same projection, so the SPA replaces its session
        // signal from this response instead of re-fetching.
        return Results.Ok(await CurrentUserBuilder.BuildCurrentUserAsync(user, userManager, members));
    }
}
