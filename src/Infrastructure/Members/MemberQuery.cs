using Microsoft.EntityFrameworkCore;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Infrastructure.Members;

/// <summary>
/// Infrastructure side of <see cref="IMemberQuery"/> — the admin's full member list (FR-005) and the
/// single-member read behind the edit form.
///
/// <para>
/// PROJECTED FROM MEMBERS, LEFT-JOINED TO ACCOUNTS since S-14, and the direction matters: a member may
/// have no account, so driving the query from <c>db.Users</c> would silently omit exactly the people
/// this slice exists to show. AsNoTracking and projected in the database, so the list costs a few
/// columns rather than whole rows.
/// </para>
///
/// <para>
/// ADMINS ARE NOT EXCLUDED, and the protection that exclusion once provided has MOVED rather than
/// vanished — read this before "restoring" the filter. It used to drop admins structurally, because
/// the seeded admin is an ordinary account in the same table and blocking the only admin locks the
/// club out of its own app. S-04 needs admins visible: prd-v2 FR-003 requires an owner who teaches to
/// be grantable the Trainer role, and the member list is the surface that grant lives on. So the list
/// returns everyone, and the sole remaining guard is the is_admin check in
/// MemberAdminEndpoints.BlockAsync, which refuses the block itself.
/// </para>
/// </summary>
public class MemberQuery(AppDbContext db) : IMemberQuery
{
    public async Task<IReadOnlyList<MemberSummary>> GetMembersAsync(
        MemberListFilter? filter,
        CancellationToken cancellationToken)
    {
        var members = Filtered(db.Members.AsNoTracking(), filter);

        var rows = await members
            .OrderBy(m => m.DisplayName)
            .Select(m => new
            {
                m.Id,
                m.UserId,
                m.DisplayName,
                m.Email,
                MembershipStatus = m.Status,

                // Cast to a nullable so "no account" and a real status stay distinguishable. Without
                // it the projection would yield the enum's default for a member with no account -
                // which is Pending, a real status, and a lie.
                AccountStatus = (AccountStatus?)(m.User == null ? null : m.User.Status),

                HasAccessCode = m.AccessCode != null,
                m.CreatedAt,

                // Roles come back as a correlated collection projection. What this buys is ONE
                // round-trip: under EF's default SingleQuery behaviour the whole thing is a single
                // statement. It does NOT avoid a join or a client-side regroup - EF emits a LEFT JOIN
                // and regroups in memory either way, which is fine at one-club scale.
                //
                // Name, not NormalizedName — this is what crosses the wire to the screen, and Identity
                // stores the display form in Name. (Comparisons still use NormalizedName; see
                // ChangeTrainerRoleAsync's IsInRoleAsync, which normalises its argument.)
                Roles = (from userRole in db.UserRoles
                         join role in db.Roles on userRole.RoleId equals role.Id
                         where userRole.UserId == m.UserId
                         select role.Name).ToList(),
            })
            .ToListAsync(cancellationToken);

        // Enum -> name after materialising: ToString() on an enum has no SQL translation, and forcing
        // one would cost more than mapping a short list in memory.
        return rows
            .Select(r => new MemberSummary(
                r.Id,
                r.UserId,
                r.DisplayName,
                r.Email,
                r.MembershipStatus.ToString(),
                r.AccountStatus?.ToString(),
                r.Roles.Where(name => name is not null).Select(name => name!).ToList(),
                r.HasAccessCode,
                r.CreatedAt))
            .ToList();
    }

    public async Task<MemberDetail?> FindDetailAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var row = await db.Members
            .AsNoTracking()
            .Where(m => m.Id == memberId)
            .Select(m => new
            {
                m.Id,
                m.UserId,
                m.DisplayName,
                m.Email,
                MembershipStatus = m.Status,
                AccountStatus = (AccountStatus?)(m.User == null ? null : m.User.Status),
                m.PhoneNumber,
                m.Street,
                m.HouseNumber,
                m.PostalCode,
                m.City,
                m.CreatedAt,
                Roles = (from userRole in db.UserRoles
                         join role in db.Roles on userRole.RoleId equals role.Id
                         where userRole.UserId == m.UserId
                         select role.Name).ToList(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        return new MemberDetail(
            row.Id,
            row.UserId,
            row.DisplayName,
            row.Email,
            row.MembershipStatus.ToString(),
            row.AccountStatus?.ToString(),
            row.Roles.Where(name => name is not null).Select(name => name!).ToList(),
            row.PhoneNumber,
            row.Street,
            row.HouseNumber,
            row.PostalCode,
            row.City,
            row.CreatedAt);
    }

    /// <summary>
    /// Checks BOTH tables, because either one can make an address unavailable: a member row holds it
    /// under IX_Members_Email, and an account holds it under Identity's own uniqueness. Checking only
    /// Members would let an admin record an address that a later claim could never attach to.
    /// </summary>
    public async Task<bool> EmailExistsAsync(
        string email,
        Guid? exceptMemberId,
        CancellationToken cancellationToken)
    {
        if (await db.Members.AnyAsync(
                m => m.Email == email && (exceptMemberId == null || m.Id != exceptMemberId),
                cancellationToken))
        {
            return true;
        }

        // Identity compares on the NORMALISED column, and normalisation is upper-invariant by default
        // (Program.cs registers no custom normaliser). Matching that here rather than comparing raw
        // addresses is what stops "Anna@x.pl" from being accepted next to "anna@x.pl".
        var normalised = email.ToUpperInvariant();

        return await db.Users.AnyAsync(
            u => u.NormalizedEmail == normalised
                 && (exceptMemberId == null
                     || !db.Members.Any(m => m.Id == exceptMemberId && m.UserId == u.Id)),
            cancellationToken);
    }

    /// <summary>
    /// Turns a filter position into a predicate. The positions are the admin's vocabulary, not a
    /// projection of either enum — see <see cref="MemberListFilter"/> for why they overlap the two
    /// underlying statuses instead of mirroring one.
    /// </summary>
    private static IQueryable<Member> Filtered(IQueryable<Member> members, MemberListFilter? filter) =>
        filter switch
        {
            // A login awaiting approval. Necessarily has an account.
            MemberListFilter.Pending =>
                members.Where(m => m.User != null && m.User.Status == AccountStatus.Pending),

            // May use the club. "Active if there is an account at all" is what makes this mean the
            // same thing for a member with a login and a member without one.
            MemberListFilter.Active =>
                members.Where(m => m.Status == MembershipStatus.Active
                                   && (m.User == null || m.User.Status == AccountStatus.Active)),

            // Membership alone answers this: since S-14 a block sets both statuses together, so there
            // is no blocked account whose membership is still active.
            MemberListFilter.Blocked =>
                members.Where(m => m.Status == MembershipStatus.Blocked),

            // Seeks IX_Members_UserId's filtered form in reverse - a scan of the rows the index leaves
            // out. Small either way at one-club scale.
            MemberListFilter.WithoutAccount =>
                members.Where(m => m.UserId == null),

            _ => members,
        };
}
