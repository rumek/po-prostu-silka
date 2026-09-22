using Microsoft.EntityFrameworkCore;
using po_prostu_silka.Application.Paging;
using po_prostu_silka.Application.Training;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Training;
using po_prostu_silka.Infrastructure.Members;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Infrastructure.Training;

/// <summary>
/// Read side of the training-plan surface. Projects straight into the DTOs so each screen costs one
/// statement, and never tracks - callers here only render.
/// </summary>
public class TrainingPlanQuery(AppDbContext db) : ITrainingPlanQuery
{
    /// <summary>
    /// The one definition of "may be assigned a plan", shared by the trainer's member list (S-22) and
    /// the write-side validation, so the two cannot drift into disagreeing about who is eligible.
    ///
    /// <para>
    /// AN ACCOUNTLESS MEMBER IS ASSIGNABLE (S-14, AM-006): the predicate is "active membership, and an
    /// active account if there is one at all", so a person the club recorded but who never registered
    /// is offered, and a blocked account is not. It once read <c>db.Users</c>, which by construction
    /// could never offer the first kind. (It also fed the member picker, retired in S-22.)
    /// </para>
    ///
    /// <para>
    /// STAFF ARE NOT ASSIGNABLE (S-25). A trainer or an admin holds no plan, so the list stops offering
    /// them and the write path refuses them - both through this one predicate, via
    /// <see cref="StaffPredicate"/>.
    /// </para>
    /// </summary>
    private IQueryable<Member> Assignable(IQueryable<Member> members) =>
        Active(members).Where(StaffPredicate.IsNotStaff(db));

    /// <summary>The status half of <see cref="Assignable"/>: active membership, active account if any.</summary>
    private static IQueryable<Member> Active(IQueryable<Member> members) =>
        members.Where(x => x.Status == MembershipStatus.Active
                           && (x.User == null || x.User.Status == AccountStatus.Active));

    /// <summary>
    /// Eligible, then searched by name, then counted, then ordered and paged — and only then
    /// projected, so the plan-name lookup runs for the page's rows rather than for every match.
    ///
    /// <para>
    /// NO STAFF (S-25, reversing S-22's "admins train too"). Every account has a member row, but
    /// <see cref="Assignable"/> excludes anyone holding Trainer or Admin, so neither the trainer
    /// themselves nor any admin is on this list. That also makes it exactly the set staff may BOOK,
    /// which is why the schedule's booking picker reuses it for a trainer.
    /// </para>
    ///
    /// <para>
    /// THE PLAN NAME IS A CORRELATED LOOKUP of the member's one ACTIVE plan, which
    /// IX_TrainingPlans_MemberId_Active turns into a seek. The filtered unique index is also what makes
    /// "the" active plan well defined: there is never a second one to choose between.
    /// </para>
    /// </summary>
    public async Task<PagedResult<TrainerMemberSummary>> GetTrainerMembersAsync(
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var members = MemberSearch.ByName(Assignable(db.Members.AsNoTracking()), search);

        var total = await members.CountAsync(cancellationToken);

        var items = await members
            // The id tiebreak keeps two members with one name from landing on both sides of a page
            // boundary, as on the admin's list.
            .OrderBy(x => x.DisplayName)
            .ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new TrainerMemberSummary(
                x.Id,
                x.DisplayName,
                x.UserId != null,
                db.TrainingPlans
                    .Where(p => p.MemberId == x.Id && p.Status == TrainingPlanStatus.Active)
                    .Select(p => p.Name)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

        return new PagedResult<TrainerMemberSummary>(items, total, page, pageSize);
    }

    public Task<AssignableMember?> FindMemberAsync(Guid memberId, CancellationToken cancellationToken) =>
        db.Members
            .AsNoTracking()
            .Where(x => x.Id == memberId)
            .Select(x => new AssignableMember(x.Id, x.DisplayName, x.UserId != null))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<TrainingPlanDetail?> FindDetailAsync(Guid id, CancellationToken cancellationToken) =>
        ProjectDetail(db.TrainingPlans.Where(x => x.Id == id)).FirstOrDefaultAsync(cancellationToken);

    public Task<TrainingPlanDetail?> FindActiveForMemberAsync(
        Guid memberId,
        CancellationToken cancellationToken) =>
        ProjectDetail(db.TrainingPlans
                .Where(x => x.MemberId == memberId && x.Status == TrainingPlanStatus.Active))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<MemberAssignability> IsAssignableAsync(Guid memberId, CancellationToken cancellationToken)
    {
        // Three questions in one round trip: does the member exist, are they active, are they staff.
        // Materialised as a list and read with FirstOrDefault so "no such member" is an empty list
        // rather than a row of falses — the caller answers each case with its own reason.
        //
        // THE ELIGIBILITY HALF GOES THROUGH Assignable, as a membership test rather than a restated
        // predicate. Writing the same condition out again here would have made the "one definition"
        // Assignable claims a comment rather than a fact, and left the member list free to drift from
        // the validation that is supposed to agree with it. It costs a correlated EXISTS over the
        // primary key, in the query that was already being issued.
        var rows = await db.Members
            .AsNoTracking()
            .Where(x => x.Id == memberId)
            .Select(x => new
            {
                IsActive = Active(db.Members).Any(a => a.Id == x.Id),
                IsAssignable = Assignable(db.Members).Any(a => a.Id == x.Id),
            })
            .ToListAsync(cancellationToken);

        var row = rows.FirstOrDefault();

        return row switch
        {
            null => MemberAssignability.NotFound,
            { IsActive: false } => MemberAssignability.NotActive,
            { IsAssignable: false } => MemberAssignability.Staff,
            _ => MemberAssignability.Assignable,
        };
    }

    public Task<ExerciseSummary?> FindPlanExerciseAsync(
        Guid memberId,
        Guid exerciseId,
        CancellationToken cancellationToken) =>
        // The join IS the authorization: an exercise resolves only through a plan item belonging to
        // this member's active plan. There is deliberately no IsActive filter - a plan keeps showing
        // an exercise the library retired after it was assigned.
        db.TrainingPlanItems
            .AsNoTracking()
            .Where(x =>
                x.ExerciseId == exerciseId
                && db.TrainingPlans.Any(p =>
                    p.Id == x.TrainingPlanId
                    && p.MemberId == memberId
                    && p.Status == TrainingPlanStatus.Active))
            .Select(x => new ExerciseSummary(
                x.Exercise.Id,
                x.Exercise.Name,
                x.Exercise.Description,
                x.Exercise.MuscleGroup,
                x.Exercise.Difficulty,
                x.Exercise.Equipment,
                x.Exercise.Preparation,
                x.Exercise.StartingPosition,
                x.Exercise.Execution,
                x.Exercise.VideoId,
                x.Exercise.IsActive,
                x.Exercise.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// The one projection both detail reads use, so the trainer's edit load and the member's screen
    /// cannot drift apart. Items are ordered by Position here rather than by the caller - the order
    /// IS the plan.
    /// </summary>
    private static IQueryable<TrainingPlanDetail?> ProjectDetail(IQueryable<TrainingPlan> source) =>
        source
            .AsNoTracking()
            .Select(x => new TrainingPlanDetail(
                x.Id,
                x.Name,
                x.MemberId,
                x.Member!.DisplayName,
                x.AssignedBy!.DisplayName,
                x.CreatedAt,
                x.Items
                    .OrderBy(i => i.Position)
                    .Select(i => new TrainingPlanItemView(
                        i.Id,
                        i.ExerciseId,
                        i.Exercise.Name,
                        i.Position,
                        i.Sets,
                        i.Reps,
                        i.WeightKg,
                        i.RestSeconds,
                        i.Note,
                        i.DurationSeconds,
                        // A projection, not an Include - the same navigation this method already
                        // traverses for i.Exercise.Name, so it costs no extra join.
                        i.Exercise.MuscleGroup))
                    .ToList()));
}
