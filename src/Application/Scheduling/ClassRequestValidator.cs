using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// Shape validation for the occurrence write paths, with the bounds it enforces.
///
/// <para>
/// THESE FOUR BOUNDS ARE DUPLICATED FROM ClassTypeValidator ON PURPOSE, not shared from it. An
/// occurrence may legitimately override its type's defaults (prd-v2 FR-008), so it cannot
/// inherit the type's bounds by reference any more than it inherits its numbers - the whole
/// point is that the two values are independent after creation. Keep the four constants in
/// step by hand.
/// </para>
/// </summary>
public static class ClassRequestValidator
{
    /// <summary>
    /// Bounds on an occurrence's own duration and capacity.
    ///
    /// <para>
    /// DUPLICATED FROM ClassTypeEndpoints ON PURPOSE, not shared through it. An occurrence may
    /// legitimately override its type's defaults (prd-v2 FR-008), so it cannot inherit the type's
    /// bounds by reference any more than it inherits its numbers — the whole point is that the two
    /// values are independent after creation. Keep the four constants in step by hand.
    /// </para>
    /// </summary>
    private const int MinDurationMinutes = 1;

    private const int MaxDurationMinutes = 480;

    private const int MinCapacity = 1;

    private const int MaxCapacity = 200;

    /// <summary>
    /// The rules shared by create and edit. Hand-rolled, like every other validation in this
    /// codebase — there is no validation library here and adding one for five fields is not
    /// warranted.
    /// </summary>
    public static IResult? Validate(ClassRequest request)
    {
        // A reference that is absent, not a field that is blank: the client picks these from two
        // lists, so the only way they arrive empty is a form submitted without a selection.
        if (request.ClassTypeId == Guid.Empty || request.InstructorMemberId == Guid.Empty)
        {
            return Results.Json(new ClassFailure("missing_field"), statusCode: 400);
        }

        // A class nobody can book is not a class. The ceiling is far above any room this club has and
        // exists only to catch a slipped digit.
        if (request.Capacity < MinCapacity || request.Capacity > MaxCapacity)
        {
            return Results.Json(new ClassFailure("invalid_capacity"), statusCode: 400);
        }

        // Zero-length would make every overlap check meaningless. The ceiling is eight hours: past
        // that it is a typo (600 for 60), not a class.
        if (request.DurationMinutes < MinDurationMinutes
            || request.DurationMinutes > MaxDurationMinutes)
        {
            return Results.Json(new ClassFailure("invalid_duration"), statusCode: 400);
        }

        return null;
    }

    /// <summary>
    /// Resolves and vets the instructor the request names (prd-v2 FR-009): the member must exist,
    /// hold an account, and that account must be ACTIVE and in the Trainer role.
    ///
    /// <para>
    /// TAKES A MEMBER ID AND CHECKS THE ACCOUNT BEHIND IT (S-14). The rule itself is unchanged — an
    /// instructor must hold an active account with the Trainer role — but the identifier the client
    /// submits is now a member, like every other identifier on this surface. A member with no account
    /// is refused as <c>unknown_instructor</c>: moving the foreign key made an accountless instructor
    /// representable, not permitted, and closing roadmap Open Question 3 is a deliberate later
    /// decision rather than a side effect of this migration.
    /// </para>
    ///
    /// <para>
    /// The refusal is ONE code for every reason — no such member, no account, blocked or pending, not
    /// a trainer's account — because this surface must not become a way to probe which member ids
    /// exist or what state somebody's login is in. It is also the whole of the useful advice: the
    /// picker only ever offers active trainers, so any of these means the client is working from a
    /// stale list and should pick somebody else.
    /// </para>
    ///
    /// <para>
    /// The role check goes through UserManager rather than a query seam, matching
    /// MemberAdminEndpoints.GrantTrainerAsync — <c>IsInRoleAsync</c> normalises its argument, so the
    /// role name is compared the way Identity stores it. A single lookup does not justify a third
    /// read seam.
    /// </para>
    ///
    /// <para>
    /// RETURNS THE ACCOUNT, not just a verdict. The caller needs its DisplayName to build the
    /// response, and this method has already fetched it - handing it back is what lets both write
    /// paths answer without a second round-trip to the database after they have committed.
    /// </para>
    /// </summary>
    /// <returns>
    /// <c>Failure</c> set and <c>Instructor</c> null when the member may not be assigned; the
    /// reverse when they may. Exactly one of the two is ever non-null.
    /// </returns>
    public static async Task<(IResult? Failure, ApplicationUser? Instructor)>
        ValidateInstructorAsync(
            Guid instructorMemberId,
            UserManager<ApplicationUser> userManager,
            IMemberStore members,
            CancellationToken cancellationToken)
    {
        var member = await members.FindAsync(instructorMemberId, cancellationToken);

        if (member is null
            || member.Status != MembershipStatus.Active
            || member.UserId is null)
        {
            return (Results.Json(new ClassFailure("unknown_instructor"), statusCode: 400), null);
        }

        var instructor = await userManager.FindByIdAsync(member.UserId);

        if (instructor is null || instructor.Status != AccountStatus.Active)
        {
            return (Results.Json(new ClassFailure("unknown_instructor"), statusCode: 400), null);
        }

        if (!await userManager.IsInRoleAsync(instructor, ApplicationRoles.Trainer))
        {
            return (Results.Json(new ClassFailure("instructor_not_trainer"), statusCode: 400), null);
        }

        return (null, instructor);
    }
}
