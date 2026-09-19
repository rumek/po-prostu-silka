using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// The resource-level half of staff booking authorization.
///
/// <para>
/// THE GROUP POLICY ALONE IS NOT SUFFICIENT. TrainerOrAdmin admits every trainer in the club,
/// and a trainer may act only on the classes they personally instruct (MP-02). That narrowing
/// cannot live in the policy: it depends on the CLASS in the route, which no policy can see.
/// So every handler on the staff group calls <see cref="MayActOn"/>, and ANY endpoint added
/// to that group must call it too - a new route inherits the group's admission and none of
/// its narrowing, which would silently let a trainer act on somebody else's class.
/// </para>
///
/// <para>
/// Inline rather than an IAuthorizationHandler because that is what this codebase does
/// everywhere: authorization here is either a group policy or a hand-rolled field check, and
/// nothing in this repository registers a resource handler. This is the known cost of that
/// choice, written down where the next person will read it. The registration site in
/// BookingEndpoints carries the same warning.
/// </para>
/// </summary>
internal static class BookingAuthorization
{
    /// <summary>
    /// Whether this caller may act on this class (S-16, MP-02).
    ///
    /// <para>
    /// THE FIRST RESOURCE-OWNERSHIP CHECK IN THIS CODEBASE. Until now every authorization decision
    /// here was either a group policy or a field check about the CALLER; this one compares the caller
    /// against a property of the resource, which is why it could not stay in the policy — a policy
    /// cannot see the class in the route.
    /// </para>
    ///
    /// <para>
    /// An ADMIN PASSES UNCONDITIONALLY and is checked first, so an owner who also teaches is never
    /// narrowed to their own classes by holding the Trainer role as well. Roles are additive in this
    /// product (see <see cref="ApplicationRoles"/>) and the realistic staff account holds both.
    /// </para>
    /// </summary>
    public static bool MayActOn(ClaimsPrincipal principal, Class entity) =>
        principal.IsInRole(ApplicationRoles.Admin)
        || principal.GetMemberId() == entity.InstructorMemberId;

    /// <summary>
    /// 403, NOT 404, when a trainer reaches for a class they do not instruct.
    ///
    /// <para>
    /// The class's existence is not a secret — every member can see it on the schedule, instructor
    /// included. What is refused is the ACTION, and saying so is the honest answer. Hiding it behind a
    /// 404 would also make the SPA's error handling wrong: "this class is gone, refresh" and "this is
    /// not your class" call for different screens.
    /// </para>
    /// </summary>
    public static IResult NotYourClass() => Results.Forbid();
}
