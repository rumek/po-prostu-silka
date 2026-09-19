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
/// Signs a member in and issues the session cookie.
/// </summary>
public static class Login
{
    public static async Task<IResult> HandleAsync(
        [FromBody] LoginRequest request,
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IMemberStore members)
    {
        // The record's strings are non-nullable, but {"email": null} deserialises to null all the
        // same - nullable reference types are a compile-time contract, not a runtime one - and
        // FindByEmailAsync would then throw ArgumentNullException on an anonymous endpoint. Answer
        // the same non-disclosing 401 a wrong address gets, rather than a 500.
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.Json(new LoginFailure("invalid_credentials"), statusCode: 401);
        }

        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return Results.Json(new LoginFailure("invalid_credentials"), statusCode: 401);
        }

        // Password is checked BEFORE status, deliberately. Reporting "pending" or "blocked" to
        // someone who has not proved they own the account would leak both that the address is
        // registered and what state it is in.
        //
        // ASYMMETRY, ON PURPOSE: /register DOES disclose that an address is taken (409 email_taken).
        // Do not "fix" one endpoint to match the other - see RegisterAsync for why registration
        // chooses disclosure and login chooses silence.
        var passwordResult = await signInManager.CheckPasswordSignInAsync(
            user, request.Password, lockoutOnFailure: true);

        if (!passwordResult.Succeeded)
        {
            return Results.Json(new LoginFailure("invalid_credentials"), statusCode: 401);
        }

        // BLOCKED IS THE ONLY REFUSAL LEFT (S-16). There used to be a Pending branch here that let
        // an unapproved member sign in and land on an awaiting-approval screen; nothing produces
        // Pending any more, so the branch was unreachable and is gone. Blocked stays refused -
        // handing a 30-day cookie to someone whose access was revoked inverts what Blocked is for.
        if (user.Status == AccountStatus.Blocked)
        {
            return Results.Json(new LoginFailure("blocked"), statusCode: 401);
        }

        // isPersistent: true is what makes the 30-day window survive closing the browser - without
        // it the cookie is a session cookie and mobile members re-login constantly (PRD FR-002).
        await signInManager.SignInAsync(user, isPersistent: true);

        return Results.Ok(await CurrentUserBuilder.BuildCurrentUserAsync(user, userManager, members));
    }
}
