using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Training;

namespace po_prostu_silka.Application.Training;

/// <summary>
/// Turns validated request items into plan-item entities, and the write retry bound.
/// </summary>
public static class TrainingPlanItemBuilder
{
    /// <summary>
    /// How many times assignment re-reads and retries before giving up.
    ///
    /// <para>
    /// TEN, matching BookingEndpoints, and for a weaker version of the same reason. Each racer that
    /// commits rotates the archived plan's stamp and costs every other racer one attempt. Contention
    /// here is far lower than on a popular class - two trainers assigning to the same member in the
    /// same instant is already unusual - but the bound costs nothing when it is not reached, and a
    /// number chosen to be "obviously enough" is how the booking path first got it wrong.
    /// </para>
    ///
    /// <para>
    /// It cannot spin: every losing attempt re-reads and either finds the winner's plan to archive or
    /// finds none, and both paths make progress. Exhausting ten means something other than contention
    /// is wrong, and <c>conflict</c> tells the trainer to try again rather than showing a 500.
    /// </para>
    /// </summary>
    public const int MaxAttempts = 10;

    /// <summary>
    /// Numbers the request's items from the array order. The ONLY place Position is assigned.
    ///
    /// <para>
    /// The rows come back unattached, with no TrainingPlanId - the CREATE path lets navigation fixup
    /// fill it in on a brand-new graph, and the EDIT path has the store set it explicitly. Do not
    /// hand these to a tracked parent's collection navigation; see UpdateAsync for what that cost.
    /// </para>
    /// </summary>
    public static List<TrainingPlanItem> BuildItems(TrainingPlanRequest request) =>
        [.. request.Items.Select((item, index) => new TrainingPlanItem
        {
            Id = Guid.NewGuid(),
            ExerciseId = item.ExerciseId,
            Position = index,
            Sets = item.Sets,
            Reps = TrainingPlanValidator.Normalize(item.Reps),
            WeightKg = item.WeightKg,
            RestSeconds = item.RestSeconds,
            DurationSeconds = item.DurationSeconds,
            Note = TrainingPlanValidator.Normalize(item.Note),
        })];
}
