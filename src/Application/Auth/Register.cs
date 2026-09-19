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
/// Registration through an invitation code (IR-01..IR-06).
///
/// <para>
/// THE LARGEST HANDLER IN THE REPOSITORY, moved as one body. Decomposing it is a separate
/// piece of work with its own risk; S-18 promised to change no behaviour and that promise is
/// easiest to keep by not touching the inside of this method at all.
/// </para>
///
/// <para>
/// THE LOGGER CATEGORY IS PINNED to typeof(AuthEndpoints) rather than this class. The
/// category is a string that reaches Azure log queries, so it is observable behaviour and
/// CS-03 covers it. Do not "tidy" it to typeof(Register).
/// </para>
/// </summary>
public static class Register
{
    /// <summary>
    /// Creates an ACTIVE account and signs it in immediately (S-16, MP-03).
    ///
    /// <para>
    /// THE ACCOUNT WORKS THE MOMENT IT EXISTS. Approval is gone: a new member reaches the dashboard
    /// straight away and sees the schedule, their (empty) karnet and their (empty) plan. What they
    /// cannot do is train, because being booked into a class requires a karnet the club issues at the
    /// desk - see MembershipPass. The gate moved; it did not disappear.
    /// </para>
    ///
    /// <para>
    /// ASYMMETRY, ON PURPOSE: this endpoint discloses that an email is already registered, while
    /// /login deliberately refuses to distinguish a wrong password from an unknown address. The trade
    /// SURVIVES S-16 but its justification had to be restated, because the old one no longer holds:
    /// it used to be that silence would strand a member waiting forever for an approval that was
    /// never coming, and there is no approval now. What remains is plainer and still decisive - with
    /// no email-confirmation flow in scope, silence tells a member who forgot they had signed up to
    /// keep retrying a password that will never work, with nothing anywhere to explain why. For a
    /// single gym, "this address belongs to a member here" is close to worthless to an attacker.
    /// Do not align the two endpoints without re-deciding that.
    /// </para>
    ///
    /// <para>
    /// RATE LIMITED, per client IP, and that is the anti-spam control now. It used to be the approval
    /// gate itself, whose accepted cost was junk accumulating as Pending rows nobody would approve;
    /// once registration produces a working account that cost changes shape entirely. See
    /// RateLimitPolicies.Register for what the limiter does and does not promise.
    /// </para>
    /// </summary>
    public static async Task<IResult> HandleAsync(
        [FromBody] RegisterRequest request,
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IMemberStore members,
        IMemberQuery memberQuery,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory)
    {
        // Same runtime-vs-compile-time gap as LoginAsync: a null in the JSON body reaches here
        // despite the non-nullable record, and FindByEmailAsync would throw. Answer in this
        // endpoint's own vocabulary instead of 500-ing.
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return Results.Json(new RegisterFailure("invalid_email"), statusCode: 400);
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.Json(new RegisterFailure("invalid_password"), statusCode: 400);
        }

        // THE CODE IS RESOLVED BEFORE ANYTHING IS CREATED, and since S-17 it is also REQUIRED: this
        // block sits where the display-name and contact-detail validation used to, so a codeless
        // request still costs one round trip and leaves no account behind.
        //
        // A missing code and a malformed one answer the same 400. They are the same failure now -
        // "what you sent could not be an invitation" - and the register screen never lets either
        // happen, because the field is prefilled from the link and readonly.
        if (!MemberAccessCode.TryNormalise(request.MemberCode, out var normalisedCode))
        {
            return Results.Json(new RegisterFailure("invalid_member_code"), statusCode: 400);
        }

        var claimed = await members.FindByAccessCodeAsync(normalisedCode, CancellationToken.None);

        // ONE ANSWER FOR EVERY CAUSE: no such code, already claimed, expired, revoked, or the
        // member blocked since it was issued. Telling them apart would confirm to a stranger that
        // a code once existed and what became of it - and a block must not be claimable around.
        if (claimed is null
            || claimed.UserId is not null
            || claimed.AccessCodeExpiresAt is null
            || claimed.AccessCodeExpiresAt <= timeProvider.GetUtcNow()
            || claimed.Status != MembershipStatus.Active)
        {
            return Results.Json(new RegisterFailure("unknown_member_code"), statusCode: 409);
        }

        // BOTH TABLES, not just Identity's. Every branch below writes request.Email into a Member row
        // guarded by IX_Members_Email, so an address the club already recorded for someone with no
        // account is unavailable even though no ACCOUNT holds it. Asking userManager alone let that
        // case through to SaveChangesAsync, where the unique violation surfaced as a 500 and the
        // compensating delete undid a real account on every retry - shutting a walk-in out of
        // registering with their own address, which is the flow this slice exists for.
        //
        // exceptMemberId is the member being claimed: their own address must not collide with itself
        // when the code they typed belongs to the row that already holds it.
        if (await memberQuery.EmailExistsAsync(request.Email, claimed.Id, CancellationToken.None))
        {
            return Results.Json(new RegisterFailure("email_taken"), statusCode: 409);
        }

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,

            // THE CLUB'S NAME, NOT A TYPED ONE (S-17). The form stopped asking, so the record is the
            // only source - and it always has one, because the admin surface refuses a blank display
            // name on both create and edit (MemberAdminEndpoints).
            DisplayName = claimed.DisplayName,
            // ACTIVE, not Pending (S-16, MP-03). AccountStatus.Pending is retired rather than
            // removed - its numeric value stays reserved - but nothing produces it any more.
            Status = AccountStatus.Active,
            CreatedAt = timeProvider.GetUtcNow(),

            // THE ADDRESS IS NOT WRITTEN HERE ANY MORE (S-14 Phase 8). It lives on the Member, which
            // is the only place it can live once a person may exist without an account at all; the
            // four columns on AspNetUsers go in the next release. PhoneNumber stays because it is
            // Identity's own column and does not go anywhere — see ProfileEndpoints for why keeping
            // it in step is worth one assignment.
            //
            // Copied FROM the claimed record since S-17, and null when the club never took one. The
            // member supplies it through /profile, which is the one place that still asks.
            PhoneNumber = claimed.PhoneNumber,
        };

        var created = await userManager.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            // Map Identity's error codes to our own vocabulary rather than forwarding its text: the
            // raw descriptions are English, unlocalised, and occasionally leak policy detail.
            var codes = created.Errors.Select(e => e.Code).ToArray();

            // Duplicate FIRST, and as a 409 rather than a 400. The FindByEmailAsync check above
            // catches this in the ordinary case, but two simultaneous registrations of the same
            // address both pass it and the loser lands here. Left to the "Email" branch below it
            // would answer 400 invalid_email - telling someone their perfectly valid address is
            // malformed, and hiding the "Zaloguj się" link the real email_taken path offers.
            if (codes.Any(c => c.StartsWith("Duplicate", StringComparison.Ordinal)))
            {
                return Results.Json(new RegisterFailure("email_taken"), statusCode: 409);
            }

            var reason = codes.Any(c => c.Contains("Password", StringComparison.Ordinal))
                ? "invalid_password"
                : codes.Any(c => c.Contains("Email", StringComparison.Ordinal)
                    || c.Contains("UserName", StringComparison.Ordinal))
                    ? "invalid_email"
                    : "invalid_registration";

            return Results.Json(new RegisterFailure(reason), statusCode: 400);
        }

        var roleAssigned = await userManager.AddToRoleAsync(user, ApplicationRoles.User);
        if (!roleAssigned.Succeeded)
        {
            // A role-less account is unrecoverable through anything this slice ships: it satisfies
            // the ActiveMember policy's status check, fails its RequireRole, and the admin surface
            // here is approve-only. Better to undo the registration and let them retry than to leave
            // an account that can be approved and still cannot do anything.
            var logger = loggerFactory.CreateLogger(typeof(AuthEndpoints));
            logger.LogError(
                "Role assignment failed for new user {UserId}; deleting the account. Errors: {Errors}",
                user.Id,
                string.Join("; ", roleAssigned.Errors.Select(e => e.Description)));

            await userManager.DeleteAsync(user);
            return Results.Problem("Registration could not be completed.", statusCode: 500);
        }

        // THE CLUB'S OWN RECORD OF THIS PERSON (S-14), linked to the account just created.
        //
        // Registration no longer PRODUCES a member - S-17 left it only able to link one - so the
        // producers that keep "an account with no member" unreachable are now the admin surface, the
        // AddMembers backfill and AdminSeeder. The guarantee itself is unchanged and still
        // load-bearing: the membership claim refuses an account without a member everywhere it is
        // checked, and the code required above is precisely what supplies one here.
        //
        // A SECOND SAVE, not part of CreateAsync's. Identity commits the account itself, so there is no
        // way to make these one write short of an explicit transaction - and an explicit transaction
        // here would have to run through Database.CreateExecutionStrategy().ExecuteAsync, because
        // EnableRetryOnFailure is on (Program.cs) and throws at RUNTIME otherwise. The compensation
        // below is the cheaper answer, and it mirrors what the role-assignment failure already does.
        // CLAIMING, NOT CREATING, and since S-17 there is no other branch: this account attaches to
        // the record the club has been keeping, so the bookings and the active plan already pointing
        // at it are simply there when the member first signs in. The branch that used to make a fresh
        // Member for a codeless registration is gone with the codeless registration itself.
        claimed.UserId = user.Id;
        claimed.ClaimedAt = user.CreatedAt;

        // Consumed. Nulling the code is what makes it single-use - there is no separate "used"
        // flag to forget to set - and the expiry goes with it, for the reason revoke gives.
        claimed.AccessCode = null;
        claimed.AccessCodeExpiresAt = null;

        // THE EMAIL IS FILLED IN, NEVER REPLACED, and this is the one contact field registration
        // still writes. It is what gives a record entered at the desk without an address - the case
        // this slice exists for - the login address as its contact; where the club already recorded
        // one, Identity's copy is authoritative and the member's own stands.
        //
        // The phone and the four address fields USED TO OVERWRITE here, on the argument that the
        // person is the better source for their own details. They stopped arriving with S-17, so the
        // club's copy is now the only one there is. Anything it lacks the member supplies through
        // /profile, which profile.html already prompts for.
        claimed.Email ??= request.Email;
        claimed.ConcurrencyStamp = Guid.NewGuid().ToString();

        try
        {
            // NOT TrySaveChangesAsync, which would swallow the one failure that matters here. The
            // unique index on Members.UserId is what settles two people racing the same code: the
            // loser's UPDATE is rejected, this throws, and the compensating delete below removes their
            // account - leaving the code consumed by the winner and the loser free to register
            // normally.
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Same reasoning as the role-assignment rollback above: an account with no member record is
            // unrecoverable through anything this endpoint ships - it would fail every policy, and
            // nothing in the admin surface creates a member for an existing account. Undo the
            // registration and let them retry rather than leave an account that can be approved and
            // still cannot do anything.
            var memberLogger = loggerFactory.CreateLogger(typeof(AuthEndpoints));
            memberLogger.LogError(
                ex,
                "Member record could not be linked for new user {UserId}; deleting the account.",
                user.Id);

            // DISCARD FIRST, OR THE COMPENSATION SILENTLY FAILS. The failed save left the rejected
            // change tracked, so DeleteAsync's own SaveChanges would re-send it, hit the same
            // violation, and throw - leaving exactly the orphaned account this block exists to remove.
            // The account itself is already committed (CreateAsync saved it), so it is re-read rather
            // than reused: the instance we hold belongs to the graph just thrown away.
            unitOfWork.DiscardChanges();

            var orphan = await userManager.FindByIdAsync(user.Id);
            if (orphan is not null)
            {
                await userManager.DeleteAsync(orphan);
            }

            return Results.Problem("Registration could not be completed.", statusCode: 500);
        }

        // Signed in immediately, and persistent for the same reason login is: the member closes the
        // tab, waits hours for approval, and must not have to re-enter credentials to check.
        await signInManager.SignInAsync(user, isPersistent: true);

        // Same shape /login returns, so the SPA has one code path for "you now have a session".
        return Results.Ok(await CurrentUserBuilder.BuildCurrentUserAsync(user, userManager, members));
    }
}
