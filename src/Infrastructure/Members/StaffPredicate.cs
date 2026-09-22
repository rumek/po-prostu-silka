using System.Linq.Expressions;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Infrastructure.Members;

/// <summary>
/// The one Infrastructure definition of "this member is staff" (S-25): their account holds a role in
/// <see cref="ApplicationRoles.Staff"/>. <see cref="MemberStore.IsStaffAsync"/> and the plan
/// eligibility rule both read it, so the write refusals and the trainer's member list cannot drift into
/// disagreeing about who counts.
///
/// <para>
/// The correlated EXISTS is <see cref="TrainerQuery"/>'s, widened to the staff set, and it compares
/// <c>NormalizedName</c> for the reason that file gives. A member with no account is never staff: roles
/// live on the account, and a person the club recorded without one can hold none.
/// </para>
/// </summary>
public static class StaffPredicate
{
    private static readonly string[] NormalizedStaffRoles =
        ApplicationRoles.Staff.Select(role => role.ToUpperInvariant()).ToArray();

    public static Expression<Func<Member, bool>> IsStaff(AppDbContext db) =>
        member => member.UserId != null
                  && db.UserRoles.Any(userRole =>
                      userRole.UserId == member.UserId
                      && db.Roles.Any(role =>
                          role.Id == userRole.RoleId && NormalizedStaffRoles.Contains(role.NormalizedName!)));

    /// <summary>The negation of <see cref="IsStaff"/>, built from it rather than restated.</summary>
    public static Expression<Func<Member, bool>> IsNotStaff(AppDbContext db)
    {
        var isStaff = IsStaff(db);
        return Expression.Lambda<Func<Member, bool>>(Expression.Not(isStaff.Body), isStaff.Parameters);
    }
}
