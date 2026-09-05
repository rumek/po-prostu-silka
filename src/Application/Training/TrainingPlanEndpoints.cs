using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Training;

namespace po_prostu_silka.Application.Training;

/// <summary>
/// One plan as the trainer's list sees it. A CONTRACT the SPA's training-plan service mirrors.
///
/// <para>
/// Trimmed rather than sharing <see cref="TrainingPlanDetail"/>'s shape, which is the opposite of
/// what <see cref="ExerciseSummary"/> does - and deliberately so. The exercise list carries a dozen
/// rows of prose because the same screen also renders the detail; this list renders none of a plan's
/// items, and shipping every item of every plan in the club to draw a table of names would be paying
/// for something no pixel uses.
/// </para>
/// </summary>
public record TrainingPlanSummary(
    Guid Id,
    string Name,
    string MemberUserId,
    string MemberDisplayName,
    string AssignedByDisplayName,
    DateTimeOffset CreatedAt,
    int ItemCount);

/// <summary>
/// One exercise inside a plan, as every screen reads it.
///
/// <para>
/// Carries <see cref="ExerciseName"/> denormalised from the exercise row so a plan renders in one
/// request. It does NOT carry the exercise's prose or video: the member's detail screen fetches those
/// per exercise through MyPlanEndpoints, because sending eight prose fields per item would make the
/// plan payload mostly text nobody has asked to read yet.
/// </para>
/// </summary>
public record TrainingPlanItemView(
    Guid Id,
    Guid ExerciseId,
    string ExerciseName,
    int Position,
    int? Sets,
    string? Reps,
    decimal? WeightKg,
    int? RestSeconds,
    string? Note);

/// <summary>
/// A full plan with its items in order. ONE SHAPE SERVES BOTH the trainer's edit load and the
/// member's read - the two want exactly the same fields, and a second contract would be two things to
/// keep in step for no gain.
/// </summary>
public record TrainingPlanDetail(
    Guid Id,
    string Name,
    string MemberUserId,
    string MemberDisplayName,
    string AssignedByDisplayName,
    DateTimeOffset CreatedAt,
    IReadOnlyList<TrainingPlanItemView> Items);

/// <summary>
/// A member a plan may be assigned to. Deliberately two fields: the trainer's picker needs a label
/// and an id, and nothing else on this surface should expose emails or account status to an account
/// that is not an admin.
/// </summary>
public record AssignableMember(string Id, string DisplayName);

/// <summary>
/// One exercise inside a create/edit payload.
///
/// <para>
/// THERE IS NO POSITION FIELD, and its absence is the contract: the ORDER OF THE ARRAY IS THE ORDER
/// OF THE PLAN. A client that could send positions could send duplicates or gaps, and the server
/// would have to decide what those mean; numbering them here on write makes the dense, collision-free
/// sequence a property of the write path rather than a hope about the caller.
/// </para>
/// </summary>
public record TrainingPlanItemRequest(
    Guid ExerciseId,
    int? Sets,
    string? Reps,
    decimal? WeightKg,
    int? RestSeconds,
    string? Note);

/// <summary>
/// Create/edit payload. Same shape for both - an edit replaces the name and the ENTIRE item list.
///
/// <para>
/// <see cref="MemberUserId"/> is carried on edit too, and is validated to match the plan being
/// edited rather than ignored. Silently ignoring it would let a stale browser tab move a plan between
/// members and see the write succeed; refusing tells the client its state is old.
/// </para>
/// </summary>
public record TrainingPlanRequest(
    string Name,
    string MemberUserId,
    IReadOnlyList<TrainingPlanItemRequest> Items);

/// <summary>
/// Why a plan write was refused.
///
/// <para>
/// 400 (bad input): <c>missing_field</c>, <c>name_too_long</c>, <c>no_items</c>,
/// <c>too_many_items</c>, <c>invalid_sets</c>, <c>reps_too_long</c>, <c>invalid_weight</c>,
/// <c>invalid_rest</c>, <c>note_too_long</c>, <c>unknown_exercise</c>, <c>inactive_exercise</c>,
/// <c>duplicate_exercise</c>.
/// </para>
///
/// <para>
/// 409 (conflict with existing state): <c>member_not_found</c>, <c>member_not_active</c>,
/// <c>member_changed</c>, <c>conflict</c>.
/// </para>
///
/// <para>
/// Adding one here means adding it to the SPA's TrainingPlanFailure union too - that type mirrors
/// this one, and the builder maps each reason onto the control that owns it.
/// </para>
/// </summary>
public record TrainingPlanFailure(string Reason);

/// <summary>
/// Training-plan authoring (prd.md FR-015, FR-016) - the write half of S-11.
///
/// <para>
/// TRAINER OR ADMIN, with the policy applied at the GROUP as everywhere else in this codebase. This
/// is the first surface the Trainer role can reach, which retires prd-v2's "No trainer screen"
/// Non-Goal and answers its Open Question 3. prd.md FR-015 named only the admin; the rule was widened
/// rather than moved, so an admin who does not teach keeps every capability the PRD gave them.
/// </para>
///
/// <para>
/// THERE IS NO OWNERSHIP RULE. Any trainer may assign to any active member and edit any plan. This
/// product has no trainer-to-member relationship to enforce one against, and inventing one here would
/// be a data model nothing else uses. <see cref="TrainingPlan.AssignedByUserId"/> is recorded for
/// display, not for authorization - see its doc comment.
/// </para>
///
/// <para>
/// NO DELETE. Assignment archives the plan it replaces (FR-016), and nothing removes a plan, matching
/// every other aggregate here.
/// </para>
/// </summary>
public static class TrainingPlanEndpoints
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

    private const int MaxRepsLength = 50;

    private const int MaxNoteLength = 500;

    /// <summary>
    /// The ceiling on exercises in one plan. Mirrors no column - it exists so a malformed or hostile
    /// payload cannot make one request insert unbounded rows. Fifty is far above any plan a trainer
    /// writes and far below anything that would hurt.
    /// </summary>
    private const int MaxItems = 50;

    private const int MinSets = 1;

    private const int MaxSets = 20;

    private const int MinRestSeconds = 0;

    /// <summary>An hour. A longer "rest" is not a rest, it is a data-entry slip.</summary>
    private const int MaxRestSeconds = 3600;

    private const decimal MinWeightKg = 0m;

    /// <summary>What decimal(5,2) holds. Exceeding it would be a truncation error, not a rounding one.</summary>
    private const decimal MaxWeightKg = 999.99m;

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
    private const int MaxAttempts = 10;

    public static IEndpointRouteBuilder MapTrainingPlanEndpoints(this IEndpointRouteBuilder app)
    {
        var plans = app.MapGroup("/api/trainer/plans")
            .WithTags("Training")
            .RequireAuthorization(AuthorizationPolicyNames.TrainerOrAdmin);

        plans.MapGet("/", GetAllAsync);

        // BEFORE the {id:guid} route. The guid constraint would in fact save this one - "members"
        // does not parse as a Guid - but relying on a constraint to disambiguate a literal is exactly
        // the trap app.routes.ts warns about three times on the client side, and the next literal
        // added here might not be so lucky.
        plans.MapGet("/members", GetAssignableMembersAsync);

        plans.MapGet("/{id:guid}", GetByIdAsync);
        plans.MapPost("/", CreateAsync);
        plans.MapPut("/{id:guid}", UpdateAsync);

        return app;
    }

    /// <summary>
    /// Every ACTIVE plan in the club, by member name.
    ///
    /// <para>
    /// Archived plans are excluded and there is no way to ask for them: prd.md:164 cuts plan history
    /// from the MVP, so a screen that could show them does not exist. Unpaginated, for the same
    /// reason as every other admin list here - a single gym has as many active plans as it has
    /// members who train.
    /// </para>
    /// </summary>
    private static async Task<IResult> GetAllAsync(
        ITrainingPlanQuery query,
        CancellationToken cancellationToken) =>
        Results.Ok(await query.GetActiveAsync(cancellationToken));

    /// <summary>
    /// The members a plan may be assigned to: approved accounts, by display name.
    ///
    /// <para>
    /// Exists because /api/admin/members is Admin-only and a trainer needs nothing from it but a name
    /// and an id. Loosening that endpoint instead would have handed every trainer the club's email
    /// list and account statuses to draw one dropdown.
    /// </para>
    /// </summary>
    private static async Task<IResult> GetAssignableMembersAsync(
        ITrainingPlanQuery query,
        CancellationToken cancellationToken) =>
        Results.Ok(await query.GetAssignableMembersAsync(cancellationToken));

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        ITrainingPlanQuery query,
        CancellationToken cancellationToken)
    {
        var found = await query.FindDetailAsync(id, cancellationToken);

        return found is null ? Results.NotFound() : Results.Ok(found);
    }

    /// <summary>
    /// Assigns a new plan, archiving whatever the member was following (prd.md FR-016).
    ///
    /// <para>
    /// THE ONE-ACTIVE-PLAN GUARANTEE IS ENFORCED AGAINST THIS METHOD, not by it. Archiving and
    /// inserting is a read-then-write sequence committed in a single SaveChangesAsync - no explicit
    /// transaction is opened anywhere in this codebase, and opening one here would need the
    /// execution strategy because EnableRetryOnFailure is on. What keeps concurrent assignments
    /// honest is IX_TrainingPlans_Member_Active, the filtered unique index every attempt's INSERT
    /// has to get past; the loop below turns its rejection into a retry rather than a 500. The
    /// stamp rotation is the cheaper, earlier half of the same defence - see
    /// TrainingPlan.ConcurrencyStamp for why it is second and not first.
    /// </para>
    /// </summary>
    private static async Task<IResult> CreateAsync(
        TrainingPlanRequest request,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager,
        ITrainingPlanQuery query,
        ITrainingPlanStore store,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var authorUserId = userManager.GetUserId(principal);
        if (authorUserId is null)
        {
            return Results.Unauthorized();
        }

        var invalid = ValidateShape(request);
        if (invalid is not null)
        {
            return invalid;
        }

        var memberUserId = request.MemberUserId.Trim();

        var memberRefused = await ValidateMemberAsync(memberUserId, query, cancellationToken);
        if (memberRefused is not null)
        {
            return memberRefused;
        }

        var exercisesRefused = await ValidateExercisesAsync(request, store, cancellationToken);
        if (exercisesRefused is not null)
        {
            return exercisesRefused;
        }

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var now = timeProvider.GetUtcNow();

            var current = await store.FindActiveForMemberAsync(memberUserId, cancellationToken);
            if (current is not null)
            {
                current.Status = TrainingPlanStatus.Archived;
                current.ArchivedAt = now;

                // Rotated so the UPDATE that archives cannot carry a token EF already considers
                // current: a racer who lost the read then fails here instead of travelling all the
                // way down to the index. Removing this line does NOT break the race test - the
                // index still catches it - but it does leave the edit path unguarded.
                current.ConcurrencyStamp = Guid.NewGuid().ToString();
            }

            var created = new TrainingPlan
            {
                Id = Guid.NewGuid(),
                Name = request.Name.Trim(),
                MemberUserId = memberUserId,
                AssignedByUserId = authorUserId,
                Status = TrainingPlanStatus.Active,
                CreatedAt = now,
                Items = BuildItems(request),
            };

            store.Add(created);

            var outcome = await unitOfWork.TrySaveAsync(cancellationToken);
            if (outcome == SaveOutcome.Saved)
            {
                return Results.Ok(await query.FindDetailAsync(created.Id, cancellationToken));
            }

            // UniqueViolation: IX_TrainingPlans_Member_Active caught two racers whose INSERTs both
            // claimed the member's active slot. This is the usual outcome, and the reason the
            // invariant holds at all.
            // ConcurrencyConflict: the loser noticed earlier, on the UPDATE that archives the plan
            // another assignment had already archived.
            //
            // Both mean nothing was written. The tracked graph still holds the rejected insert and a
            // plan whose stamp is stale, so it has to go before the next read returns fresh state.
            unitOfWork.DiscardChanges();
        }

        return Refuse("conflict", 409);
    }

    /// <summary>
    /// Edits a plan in place: its name and its entire item list.
    ///
    /// <para>
    /// NO RETRY LOOP, and that is not an oversight. This path changes one plan's own rows and moves
    /// nothing across the one-active-plan boundary, so there is no read-then-write sequence to make
    /// atomic. The stamp is still rotated and the outcome still checked, so two trainers editing the
    /// same plan get a clean 409 instead of one silently overwriting the other.
    /// </para>
    /// </summary>
    private static async Task<IResult> UpdateAsync(
        Guid id,
        TrainingPlanRequest request,
        ITrainingPlanQuery query,
        ITrainingPlanStore store,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var invalid = ValidateShape(request);
        if (invalid is not null)
        {
            return invalid;
        }

        var entity = await store.FindAsync(id, cancellationToken);
        if (entity is null)
        {
            return Results.NotFound();
        }

        // An archived plan is not editable. It is not addressable by any screen either, so reaching
        // here means a stale tab or a hand-made request; 404 rather than 409 because from the
        // client's point of view the thing it is editing no longer exists.
        if (entity.Status != TrainingPlanStatus.Active)
        {
            return Results.NotFound();
        }

        if (!string.Equals(entity.MemberUserId, request.MemberUserId.Trim(), StringComparison.Ordinal))
        {
            return Refuse("member_changed", 409);
        }

        var exercisesRefused = await ValidateExercisesAsync(request, store, cancellationToken);
        if (exercisesRefused is not null)
        {
            return exercisesRefused;
        }

        entity.Name = request.Name.Trim();

        // REPLACED, NOT RECONCILED. Deleting every row and inserting the request's is what keeps
        // Position dense without matching rows by identity, and it is why TrainingPlanItems carries
        // no unique index on (plan, position) that a partial reorder would trip over mid-statement.
        //
        // It goes through the store rather than through entity.Items for a reason found the hard
        // way: assigning a fresh List to a TRACKED parent's collection navigation makes EF resolve
        // the new children against the entries it already holds, and it emitted an UPDATE against a
        // row the same SaveChanges had just deleted - "expected to affect 1 row(s), but actually
        // affected 0". Addressing the rows explicitly leaves nothing for collection fixup to guess.
        store.ReplaceItems(entity, BuildItems(request));

        entity.ConcurrencyStamp = Guid.NewGuid().ToString();

        var outcome = await unitOfWork.TrySaveAsync(cancellationToken);
        if (outcome != SaveOutcome.Saved)
        {
            return Refuse("conflict", 409);
        }

        return Results.Ok(await query.FindDetailAsync(entity.Id, cancellationToken));
    }

    /// <summary>
    /// Numbers the request's items from the array order. The ONLY place Position is assigned.
    ///
    /// <para>
    /// The rows come back unattached, with no TrainingPlanId - the CREATE path lets navigation fixup
    /// fill it in on a brand-new graph, and the EDIT path has the store set it explicitly. Do not
    /// hand these to a tracked parent's collection navigation; see UpdateAsync for what that cost.
    /// </para>
    /// </summary>
    private static List<TrainingPlanItem> BuildItems(TrainingPlanRequest request) =>
        [.. request.Items.Select((item, index) => new TrainingPlanItem
        {
            Id = Guid.NewGuid(),
            ExerciseId = item.ExerciseId,
            Position = index,
            Sets = item.Sets,
            Reps = Normalize(item.Reps),
            WeightKg = item.WeightKg,
            RestSeconds = item.RestSeconds,
            Note = Normalize(item.Note),
        })];

    /// <summary>
    /// Everything checkable without touching the database. Returns null when the payload is sound.
    /// </summary>
    private static IResult? ValidateShape(TrainingPlanRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.MemberUserId))
        {
            return Refuse("missing_field", 400);
        }

        if (request.Name.Trim().Length > MaxNameLength)
        {
            return Refuse("name_too_long", 400);
        }

        if (request.Items.Count == 0)
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

            if (Normalize(item.Note) is { Length: > MaxNoteLength })
            {
                return Refuse("note_too_long", 400);
            }
        }

        return null;
    }

    /// <summary>
    /// The target must exist and be approved. 409 rather than 400 on both: the payload is well formed
    /// and was true when the trainer's screen loaded - the account changed underneath it.
    /// </summary>
    private static async Task<IResult?> ValidateMemberAsync(
        string memberUserId,
        ITrainingPlanQuery query,
        CancellationToken cancellationToken)
    {
        var status = await query.FindMemberStatusAsync(memberUserId, cancellationToken);

        if (status is null)
        {
            return Refuse("member_not_found", 409);
        }

        return status == AccountStatus.Active ? null : Refuse("member_not_active", 409);
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
    private static async Task<IResult?> ValidateExercisesAsync(
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
    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IResult Refuse(string reason, int statusCode) =>
        Results.Json(new TrainingPlanFailure(reason), statusCode: statusCode);
}

/// <summary>
/// Narrow read seam over training plans, so Application does not reference EF Core (AGENTS.md
/// layering). Implemented in Infrastructure.
/// </summary>
public interface ITrainingPlanQuery
{
    /// <summary>Every active plan, by member display name. Unbounded - a single gym's list.</summary>
    Task<IReadOnlyList<TrainingPlanSummary>> GetActiveAsync(CancellationToken cancellationToken);

    /// <summary>Approved accounts as picker rows, by display name.</summary>
    Task<IReadOnlyList<AssignableMember>> GetAssignableMembersAsync(CancellationToken cancellationToken);

    /// <summary>One plan with its items in position order, or null. Active or archived.</summary>
    Task<TrainingPlanDetail?> FindDetailAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The member's active plan with its items, or null when they have none.</summary>
    Task<TrainingPlanDetail?> FindActiveForMemberAsync(string memberUserId, CancellationToken cancellationToken);

    /// <summary>
    /// The account's status, or null when no such account exists. A status rather than the row: the
    /// caller only needs to know whether a plan may be assigned, and returning ApplicationUser here
    /// would invite a write path to mutate an account through a read seam.
    /// </summary>
    Task<AccountStatus?> FindMemberStatusAsync(string memberUserId, CancellationToken cancellationToken);

    /// <summary>
    /// One exercise, but ONLY if it appears in the given member's active plan. Null otherwise -
    /// including when the exercise exists and the member simply has not been prescribed it.
    ///
    /// <para>
    /// This is how prd.md:163's "no standalone exercise library browsing" is ENFORCED rather than
    /// merely respected. The scoping is a join against the member's own plan, not a role check, so
    /// there is no way to widen it by holding a different role.
    /// </para>
    /// </summary>
    Task<ExerciseSummary?> FindPlanExerciseAsync(
        string memberUserId,
        Guid exerciseId,
        CancellationToken cancellationToken);
}

/// <summary>
/// The write counterpart. Intention-revealing methods rather than a generic repository.
///
/// No Remove: a plan is archived, never deleted. <see cref="ReplaceItems"/> is not an exception - it
/// swaps a plan's item rows for a new set, which is replacement within one aggregate rather than
/// deletion of one.
///
/// Nothing here saves. The endpoint commits through <see cref="IUnitOfWork"/>.
/// </summary>
public interface ITrainingPlanStore
{
    /// <summary>One plan, TRACKED with its items - callers mutate what comes back.</summary>
    Task<TrainingPlan?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// The member's active plan, TRACKED, without its items. The assignment path only ever flips this
    /// row's status and rotates its stamp, and loading a plan's items to archive it would be reading
    /// rows to ignore them.
    /// </summary>
    Task<TrainingPlan?> FindActiveForMemberAsync(string memberUserId, CancellationToken cancellationToken);

    void Add(TrainingPlan entity);

    /// <summary>
    /// Swaps the plan's item rows for <paramref name="items"/>, numbered as they arrive.
    ///
    /// <para>
    /// One method rather than a clear and an add, because the two are only ever correct together and
    /// splitting them invites the caller to reach for the collection navigation in between - which is
    /// exactly the mistake this signature exists to prevent (see UpdateAsync).
    /// </para>
    /// </summary>
    void ReplaceItems(TrainingPlan entity, IReadOnlyList<TrainingPlanItem> items);

    /// <summary>
    /// Which of the given exercise ids exist, and whether each is active. Ids that do not exist are
    /// ABSENT from the result rather than present as false - the caller has to tell "no such
    /// exercise" from "retired exercise", because they are different refusals.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, bool>> FindExerciseStatesAsync(
        IReadOnlyCollection<Guid> exerciseIds,
        CancellationToken cancellationToken);
}
