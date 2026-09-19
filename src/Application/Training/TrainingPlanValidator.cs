using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Training;

namespace po_prostu_silka.Application.Training;

/// <summary>
/// Validation for the plan write paths, with every bound it enforces.
///
/// <para>
/// TWELVE OF THE THIRTEEN CONSTANTS FEED ValidateShape, which is why they travel with it
/// rather than staying on the endpoint class. The thirteenth, MaxAttempts, belongs to the
/// create path's retry loop and lives with it.
/// </para>
/// </summary>
internal static class TrainingPlanValidator
{
    /// <summary>
    /// Every bound below matches a HasMaxLength or a documented range in TrainingPlanConfiguration
    /// and TrainingPlanItemConfiguration. Keep the two in step - and the Angular validators, which
    /// are the third copy.
    ///
    /// NOT optional to check. Without a guard, a longer value reaches SQL Server, which refuses the
    /// INSERT with "String or binary data would be truncated" - an unhandled DbUpdateException, i.e.
    /// a 500 for what is ordinary bad input. This is the single most repeated finding in this repo's
    /// review history.
    /// </summary>
    private const int MaxNameLength = 120;

    /// <summary>
    /// The ceiling on exercises in one plan. Mirrors no column - it exists so a malformed or hostile
    /// payload cannot make one request insert unbounded rows. Fifty is far above any plan a trainer
    /// writes and far below anything that would hurt.
    /// </summary>
    private const int MaxItems = 50;

    private const int MinSets = 1;

    private const int MaxSets = 20;

    private const int MaxRepsLength = 50;

    private const decimal MinWeightKg = 0m;

    /// <summary>
    /// What decimal(5,2) holds. Exceeding it would be a truncation error, not a rounding one.
    ///
    /// <para>
    /// THE SCALE IS NOT CHECKED, ONLY THE RANGE, and that is deliberate: a submitted 60.123 is stored
    /// as 60.12, silently. SQL Server rounds rather than truncating here, so this is a changed value
    /// rather than a failed INSERT - the hazard the other bounds guard against does not apply. Weight
    /// on a gym floor moves in half-kilograms (the builder's input carries step="0.5" as a nudge), so
    /// a third decimal place is noise, and refusing it would explain a rejection nobody meant to
    /// cause. Documented rather than enforced - if a future caller needs the entered value back
    /// exactly, widen the column, do not add a validator.
    /// </para>
    /// </summary>
    private const decimal MaxWeightKg = 999.99m;

    private const int MinRestSeconds = 0;

    /// <summary>An hour. A longer "rest" is not a rest, it is a data-entry slip.</summary>
    private const int MaxRestSeconds = 3600;

    /// <summary>
    /// ONE, NOT ZERO, and that is the one place duration does not mirror rest. A zero-second REST is
    /// a legitimate prescription - "straight into the next set" - which is why MinRestSeconds is 0.
    /// A zero-second EXERCISE is not a prescription at all, it is a slip of the keyboard.
    /// </summary>
    private const int MinDurationSeconds = 1;

    /// <summary>The same hour ceiling rest gets, and for the same reason.</summary>
    private const int MaxDurationSeconds = 3600;

    private const int MaxNoteLength = 500;

    /// <summary>
    /// Everything checkable without touching the database. Returns null when the payload is sound.
    /// </summary>
    public static IResult? ValidateShape(TrainingPlanRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.MemberId == Guid.Empty)
        {
            return Refuse("missing_field", 400);
        }

        if (request.Name.Trim().Length > MaxNameLength)
        {
            return Refuse("name_too_long", 400);
        }

        // NULL, not just empty. Items is declared non-nullable, but System.Text.Json does not honour
        // nullable reference annotations and nothing configures RespectNullableAnnotations - a body
        // that simply omits "items" binds null here, and dereferencing it would be a
        // NullReferenceException escaping as a 500 for what is ordinary bad input. This is the first
        // request body in the codebase carrying a collection, so no sibling covers it.
        if (request.Items is null || request.Items.Count == 0)
        {
            return Refuse("no_items", 400);
        }

        if (request.Items.Count > MaxItems)
        {
            return Refuse("too_many_items", 400);
        }

        // One exercise may appear at most once. Twice is far more likely to be a double-click in the
        // picker than a deliberate prescription, and letting it through would leave the member
        // looking at the same row twice with no way to tell which set of numbers applies.
        if (request.Items.Select(x => x.ExerciseId).Distinct().Count() != request.Items.Count)
        {
            return Refuse("duplicate_exercise", 400);
        }

        foreach (var item in request.Items)
        {
            if (item.Sets is { } sets && (sets < MinSets || sets > MaxSets))
            {
                return Refuse("invalid_sets", 400);
            }

            if (Normalize(item.Reps) is { Length: > MaxRepsLength })
            {
                return Refuse("reps_too_long", 400);
            }

            if (item.WeightKg is { } weight && (weight < MinWeightKg || weight > MaxWeightKg))
            {
                return Refuse("invalid_weight", 400);
            }

            if (item.RestSeconds is { } rest && (rest < MinRestSeconds || rest > MaxRestSeconds))
            {
                return Refuse("invalid_rest", 400);
            }

            if (item.DurationSeconds is { } duration
                && (duration < MinDurationSeconds || duration > MaxDurationSeconds))
            {
                return Refuse("invalid_duration", 400);
            }

            if (Normalize(item.Note) is { Length: > MaxNoteLength })
            {
                return Refuse("note_too_long", 400);
            }
        }

        return null;
    }

    /// <summary>
    /// The target must exist and be eligible. 409 rather than 400 on both: the payload is well formed
    /// and was true when the trainer's screen loaded — the member changed underneath it.
    ///
    /// <para>
    /// ELIGIBLE MEANS "MAY USE THE CLUB", which since S-14 is an active membership AND, if they have a
    /// login at all, an approved one. A person the admin recorded who never registered is eligible —
    /// that is AM-006, and the point of the slice. A self-registered account still waiting for
    /// approval is NOT, exactly as before: nothing about splitting the entity was meant to let a plan
    /// be assigned to somebody nobody has vetted.
    /// </para>
    /// </summary>
    public static async Task<IResult?> ValidateMemberAsync(
        Guid memberId,
        ITrainingPlanQuery query,
        CancellationToken cancellationToken)
    {
        var assignable = await query.IsAssignableAsync(memberId, cancellationToken);

        if (assignable is null)
        {
            return Refuse("member_not_found", 409);
        }

        return assignable.Value ? null : Refuse("member_not_active", 409);
    }

    /// <summary>
    /// Every referenced exercise must exist, and must still be ACTIVE at authoring time.
    ///
    /// <para>
    /// Note the asymmetry with the read path, which deliberately does not filter on IsActive: a
    /// retired exercise may not be added to a plan, but one already in a plan stays visible to the
    /// member. Prescribing something the library has withdrawn is a mistake; rewriting a member's
    /// plan behind their back because of library housekeeping is a worse one.
    /// </para>
    /// </summary>
    public static async Task<IResult?> ValidateExercisesAsync(
        TrainingPlanRequest request,
        ITrainingPlanStore store,
        CancellationToken cancellationToken)
    {
        var requested = request.Items.Select(x => x.ExerciseId).Distinct().ToArray();

        var known = await store.FindExerciseStatesAsync(requested, cancellationToken);

        foreach (var exerciseId in requested)
        {
            if (!known.TryGetValue(exerciseId, out var isActive))
            {
                return Refuse("unknown_exercise", 400);
            }

            if (!isActive)
            {
                return Refuse("inactive_exercise", 400);
            }
        }

        return null;
    }

    /// <summary>
    /// Trims, and collapses "absent" to a single representation - the same contract Exercise's
    /// optional prose fields carry, so no screen has to test for both null and "".
    /// </summary>
    public static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static IResult Refuse(string reason, int statusCode) =>
        Results.Json(new TrainingPlanFailure(reason), statusCode: statusCode);
}
