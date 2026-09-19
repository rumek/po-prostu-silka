using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Application.Auth;
using po_prostu_silka.Api.Auth;

namespace po_prostu_silka.Api.Endpoints.Auth;

/// <summary>
/// The authentication surface: create an account, establish a session, inspect it, refresh it,
/// end it.
///
/// Registration lands here with S-01 (registration-and-approval), whose approval half S-16 removed.
/// What a newly created account may do is therefore everything a member may do — read the schedule,
/// their classes and their karnet — and the one thing it cannot is be booked into a class, because
/// that needs a karnet the club issues at the desk.
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/login", Login.HandleAsync).AllowAnonymous();
        // RATE LIMITED SINCE S-16, and the limiter is not optional garnish: it is what took the
        // approval gate's place as the anti-spam control when registration started producing accounts
        // that work. See RateLimitPolicies.Register.
        group.MapPost("/register", Register.HandleAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Register);
        group.MapPost("/logout", Logout.HandleAsync).RequireAuthorization();

        // Bare RequireAuthorization(), NEVER the ActiveMember policy - a pending member has to be
        // able to call the one endpoint that stops them being pending.
        group.MapPost("/refresh", RefreshSession.HandleAsync).RequireAuthorization();

        // RequireAuthorization() and NOT the ActiveMember policy. The original reason (a Pending
        // member must be able to read their own status) died with approval; the rule still holds for
        // a BLOCKED session that has not yet been refused a claim, and for /profile, which an account
        // must reach to supply contact details it may not have.
        group.MapGet("/me", GetCurrentUser.HandleAsync).RequireAuthorization();

        // Bare RequireAuthorization(), not ActiveMember: a member owns their password whatever their
        // membership says, and nothing about changing it depends on being able to train.
        group.MapPost("/change-password", ChangePassword.HandleAsync).RequireAuthorization();

        // The only anonymous endpoint in this app that sends mail on demand, so it is the only one
        // that carries a rate limit (Program.cs). AllowAnonymous by definition - a member who can
        // sign in does not need it.
        group.MapPost("/forgot-password", ForgotPassword.HandleAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.ForgotPassword);

        // Not rate-limited: it sends nothing, and a wrong token is already refused. The token's
        // single-use guarantee is the security stamp, which ResetPassword.HandleAsync rotates.
        group.MapPost("/reset-password", ResetPassword.HandleAsync).AllowAnonymous();

        return app;
    }
}
