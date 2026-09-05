using Microsoft.EntityFrameworkCore;
using po_prostu_silka.Application.Training;
using po_prostu_silka.Domain;
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
            .OrderBy(x => x.Member.DisplayName)
            .Select(x => new TrainingPlanSummary(
                x.Id,
                x.Name,
                x.MemberUserId,
                x.Member.DisplayName,
                x.AssignedBy.DisplayName,
                x.CreatedAt,
                // A correlated count rather than loading the items to measure them - the list renders
                // a number, not the rows.
                x.Items.Count))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AssignableMember>> GetAssignableMembersAsync(
        CancellationToken cancellationToken) =>
        await db.Users
            .AsNoTracking()
            .Where(x => x.Status == AccountStatus.Active)
            .OrderBy(x => x.DisplayName)
            .Select(x => new AssignableMember(x.Id, x.DisplayName))
            .ToListAsync(cancellationToken);

    public Task<TrainingPlanDetail?> FindDetailAsync(Guid id, CancellationToken cancellationToken) =>
        ProjectDetail(db.TrainingPlans.Where(x => x.Id == id)).FirstOrDefaultAsync(cancellationToken);

    public Task<TrainingPlanDetail?> FindActiveForMemberAsync(
        string memberUserId,
        CancellationToken cancellationToken) =>
        ProjectDetail(db.TrainingPlans
                .Where(x => x.MemberUserId == memberUserId && x.Status == TrainingPlanStatus.Active))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<AccountStatus?> FindMemberStatusAsync(
        string memberUserId,
        CancellationToken cancellationToken)
    {
        // Projected into a nullable so "no such account" and a real status are one round trip. Casting
        // to AccountStatus? inside the projection is what makes FirstOrDefaultAsync return null for a
        // missing row rather than the enum's default, which is a real status (Pending).
        var statuses = await db.Users
            .AsNoTracking()
            .Where(x => x.Id == memberUserId)
            .Select(x => (AccountStatus?)x.Status)
            .ToListAsync(cancellationToken);

        return statuses.FirstOrDefault();
    }

    public Task<ExerciseSummary?> FindPlanExerciseAsync(
        string memberUserId,
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
                    && p.MemberUserId == memberUserId
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
                x.MemberUserId,
                x.Member.DisplayName,
                x.AssignedBy.DisplayName,
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
