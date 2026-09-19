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
/// Starts a password reset (FR-026).
///
/// <para>
/// A SEPARATE BODY FROM ResetPassword, so a separate file. The two are one user journey but
/// two mechanisms - one mints and sends a token, the other consumes it - and the file
/// boundary in this codebase is the handler body, not the journey.
/// </para>
///
/// <para>
/// THE LOGGER CATEGORY IS PINNED to typeof(AuthEndpoints); see Register for why.
/// </para>
/// </summary>
public static class ForgotPassword
{
    /// <summary>
    /// Emails a reset link, if the address belongs to an account.
    ///
    /// <para>
    /// THIS ENDPOINT ANSWERS THE SAME THING NO MATTER WHAT. 200, empty body, for a registered
    /// address, an unregistered one, a Pending account, a Blocked account, a throttled repeat and a
    /// misconfigured BaseUrl alike. Every branch below returns <c>Results.Ok()</c>, because a
    /// difference in status code or body is an account-enumeration oracle. F-02's implementation
    /// review flagged exactly this shape on /login
    /// (context/archive/2026-08-31-auth-identity-foundation/reviews/impl-review.md:93-101).
    /// </para>
    ///
    /// <para>
    /// WHAT IS NOT EQUALISED: LATENCY. The unknown-address and throttled branches return early,
    /// before the token mint, the render and the commit that the found-user path performs, so a
    /// registered address costs measurably more than an unregistered one. That is a known, accepted
    /// gap - the plan asked for comparable work and this does not deliver it (see the "Adapted
    /// during implementation." note on Phase 4 item 6 in
    /// context/changes/member-profile-edit/plan.md). Exploiting it needs many samples through a
    /// 5-per-minute cap and hosting jitter; equalising it would mean holding an anonymous request on
    /// a thread for a fixed budget, on the one endpoint an anonymous caller can spam. Do not
    /// "restore" the guarantee by deleting this paragraph - either close the gap or leave both.
    /// </para>
    ///
    /// <para>
    /// ASYMMETRY WITH /register, ON PURPOSE. Registration discloses <c>email_taken</c> because
    /// silence would strand a real member. Here silence strands nobody: someone who mistypes their
    /// address simply gets no email and tries again. This endpoint sits on /login's side of that
    /// line.
    /// </para>
    ///
    /// <para>
    /// STATUS IS NOT CHECKED. A Blocked member can request a reset and use it. Branching on status
    /// would reintroduce the oracle from the other direction, and a blocked account is refused at
    /// /login regardless - a new password gets them nothing.
    /// </para>
    /// </summary>
    public static async Task<IResult> HandleAsync(
        [FromBody] ForgotPasswordRequest request,
        UserManager<ApplicationUser> userManager,
        IPasswordResetNotification notification,
        IPasswordResetThrottle throttle,
        IUnitOfWork unitOfWork,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return Results.Ok();
        }

        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return Results.Ok();
        }

        // Consumes the window. A refused attempt still falls through to the same Ok() below - the
        // throttle decides what is SENT, never what is ANSWERED.
        if (!throttle.TryAcquire(request.Email))
        {
            return Results.Ok();
        }

        // SWALLOWED ON PURPOSE - the one place in this app where that is correct. There is no
        // UseExceptionHandler here, so an unhandled throw would answer 500 for a registered address
        // while an unregistered one still answered 200: the enumeration oracle this endpoint exists
        // to deny, appearing exactly under the load that makes it easiest to measure. SQL throttling
        // on Basic DTU is the realistic trigger. The member gets no email and retries; nobody learns
        // anything from the response.
        try
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            notification.Notify(user, token);

            // Notify enqueues without saving, like every other notification here, so the outbox row
            // only exists after this commit.
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            loggerFactory
                .CreateLogger(typeof(AuthEndpoints))
                .LogError(
                    exception,
                    "Password reset could not be queued. The caller was answered normally so the "
                    + "failure does not disclose whether the address is registered.");
        }

        return Results.Ok();
    }
}
