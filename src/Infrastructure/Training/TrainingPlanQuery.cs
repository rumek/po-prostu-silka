using Microsoft.EntityFrameworkCore;
using po_prostu_silka.Application.Training;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Training;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Infrastructure.Training;

/// <summary>
/// Read side of the training-plan surface. Projects straight into the DTOs so each screen costs one
/// statement, and never tracks - callers here only render.
/// </summary>
public class TrainingPlanQuery(AppDbContext db) : ITrainingPlanQuery
{
    public async Task<IReadOnlyList<TrainingPlanSummary>> GetActiveAsync(
        CancellationToken cancellationToken) =>
        await db.TrainingPlans
            .AsNoTracking()
            .Where(x => x.Status == TrainingPlanStatus.Active)
            .OrderBy(x => x.Member!.DisplayName)
            .Select(x => new TrainingPlanSummary(
                x.Id,
                x.Name,
                x.MemberId,
                x.Member!.DisplayName,
                x.AssignedBy!.DisplayName,
                x.CreatedAt,
                // A correlated count rather than loading the items to measure them - the list renders
                // a number, not the rows.
                x.Items.Count))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// THE CHANGE THAT MAKES AN ACCOUNTLESS MEMBER ASSIGNABLE (S-14, AM-006). This used to read
    /// <c>db.Users</c> filtered on an ACTIVE ACCOUNT, which by construction could never offer a person
    /// the club recorded but who never registered. It reads members now, and the only filter left is
    /// the one that means "may use the club".
    ///
    /// <para>
    /// WHAT DID NOT CHANGE is that an unvetted account is still refused. The predicate is "active
    /// membership, and an approved account if there is one at all" — so a person the admin recorded is
    /// offered, and someone who self-registered ten minutes ago and has not been approved is not.
    /// Splitting the entity was never meant to loosen who may be given a plan.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<AssignableMember>> GetAssignableMembersAsync(
        CancellationToken cancellationToken) =>
        await Assignable(db.Members.AsNoTracking())
            .OrderBy(x => x.DisplayName)
            .Select(x => new AssignableMember(x.Id, x.DisplayName, x.UserId != null))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The one definition of "may be assigned a plan", shared by the picker and by the write-side
    /// validation so the two cannot drift into disagreeing about who is eligible.
    /// </summary>
    private static IQueryable<Member> Assignable(IQueryable<Member> members) =>
        members.Where(x => x.Status == MembershipStatus.Active
                           && (x.User == null || x.User.Status == AccountStatus.Active));

    public Task<TrainingPlanDetail?> FindDetailAsync(Guid id, CancellationToken cancellationToken) =>
        ProjectDetail(db.TrainingPlans.Where(x => x.Id == id)).FirstOrDefaultAsync(cancellationToken);

    public Task<TrainingPlanDetail?> FindActiveForMemberAsync(
        Guid memberId,
        CancellationToken cancellationToken) =>
        ProjectDetail(db.TrainingPlans
                .Where(x => x.MemberId == memberId && x.Status == TrainingPlanStatus.Active))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool?> IsAssignableAsync(Guid memberId, CancellationToken cancellationToken)
    {
        // Two questions in one round trip: does the member exist, and are they eligible. Materialised
        // as a list and read with FirstOrDefault so "no such member" comes back as null rather than as
        // false — the caller answers member_not_found and member_not_active differently, and a bool
        // alone could not tell them apart.
        var rows = await db.Members
            .AsNoTracking()
            .Where(x => x.Id == memberId)
            .Select(x => (bool?)(x.Status == MembershipStatus.Active
                                 && (x.User == null || x.User.Status == AccountStatus.Active)))
            .ToListAsync(cancellationToken);

        return rows.FirstOrDefault();
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
                        i.Note))
                    .ToList()));
}
