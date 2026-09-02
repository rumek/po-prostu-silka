using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// One class type as the admin's list and form see it. This is a CONTRACT the SPA's class-type
/// service mirrors — renaming a field breaks both screens silently.
///
/// <para>
/// <see cref="IsActive"/> crosses the wire as a bool rather than a status name, unlike
/// <see cref="ScheduledClass.Status"/>: there are exactly two states and no enum behind it, so
/// there is no numbering for a name to protect.
/// </para>
///
/// <para>
/// The two <c>Default*</c> fields keep their prefix all the way out to the client. S-06 copies them
/// onto an occurrence at creation; nothing ever resolves an occurrence's capacity through them
/// (prd-v2 FR-007), and the naming is what keeps that obvious at the call site.
/// </para>
/// </summary>
public record ClassTypeSummary(
    Guid Id,
    string Name,
    string? Description,
    int DefaultDurationMinutes,
    int DefaultCapacity,
    bool IsActive,
    DateTimeOffset CreatedAt);

/// <summary>
/// Create/edit payload. Same shape for both — an edit replaces every field, like
/// <see cref="ClassRequest"/>.
///
/// <para>
/// <c>IsActive</c> is deliberately ABSENT. Activation has its own two endpoints, so a careless edit
/// cannot silently resurrect a type the admin retired — the same reasoning that keeps block/unblock
/// off the member edit surface.
/// </para>
/// </summary>
public record ClassTypeRequest(
    string Name,
    string? Description,
    int DefaultDurationMinutes,
    int DefaultCapacity);

/// <summary>
/// Why a class-type write was refused. All 400 except <c>name_taken</c>, which is a 409 — it is a
/// conflict with existing state rather than bad input, exactly like <c>room_conflict</c>.
/// </summary>
public record ClassTypeFailure(string Reason);

/// <summary>
/// The admin's class-type definitions (prd-v2 FR-004, FR-005, FR-006, FR-007) — the layer that gives
/// a class an identity outliving any single week.
///
/// <para>
/// Admin-only, with the policy applied at the GROUP rather than per endpoint, so an endpoint added
/// here later cannot accidentally ship unauthenticated. Members never read this surface: a class
/// type reaches them through the occurrence, which is S-06's work.
/// </para>
///
/// <para>
/// NO DELETE, by design. FR-006 replaces deletion with deactivation: a retired type disappears from
/// every selection while the occurrences referencing it stay intact. An orphaned occurrence is a
/// worse failure than a hidden row.
/// </para>
///
/// <para>
/// S-05 defines and manages types. It does NOT wire them into occurrence creation — no selector, no
/// prefill, no name resolution. That is S-06.
/// </para>
/// </summary>
public static class ClassTypeEndpoints
{
    /// <summary>
    /// Bounds on a default duration. The floor matches ClassEndpoints.Validate — a zero-length class
    /// would make every overlap check meaningless. The ceiling is eight hours: past that it is a
    /// typo (600 for 60), not a class.
    /// </summary>
    private const int MinDurationMinutes = 1;

    private const int MaxDurationMinutes = 480;

    /// <summary>
    /// Bounds on a default capacity. The floor matches ClassEndpoints.Validate — a class nobody can
    /// book is not a class. The ceiling is far above any room this club has, and exists only to
    /// catch a slipped digit.
    /// </summary>
    private const int MinCapacity = 1;

    private const int MaxCapacity = 200;

    /// <summary>Matches ClassTypeConfiguration's column length. Keep the two in step.</summary>
    private const int MaxDescriptionLength = 1000;

    public static IEndpointRouteBuilder MapClassTypeEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin/class-types")
            .WithTags("Schedule")
            .RequireAuthorization(AuthorizationPolicyNames.Admin);

        admin.MapGet("/", GetAllAsync);
        admin.MapGet("/{id:guid}", GetByIdAsync);
        admin.MapPost("/", CreateAsync);
        admin.MapPut("/{id:guid}", UpdateAsync);

        // Two verbs rather than a boolean on the edit payload, for the same reason the member
        // surface exposes block/unblock instead of a status patch: the action the admin took is
        // legible in the request, and an edit cannot perform it by accident.
        admin.MapPost("/{id:guid}/deactivate", DeactivateAsync);
        admin.MapPost("/{id:guid}/activate", ActivateAsync);

        return app;
    }

    /// <summary>
    /// Every type, active and inactive, active first and then by name.
    ///
    /// UNFILTERED, deliberately. The screen's "pokaż nieaktywne" toggle filters what it already
    /// holds; a server-side flag would make every flick of that toggle a round trip. A single club's
    /// type list is a handful of rows, so there is nothing to page.
    /// </summary>
    private static async Task<IResult> GetAllAsync(
        IClassTypeQuery query,
        CancellationToken cancellationToken) =>
        Results.Ok(await query.GetAllAsync(cancellationToken));

    /// <summary>
    /// One type, for the edit form — so opening /admin/class-types/:id directly costs one row
    /// instead of the whole list. Same reasoning as ClassEndpoints.GetByIdAsync.
    /// </summary>
    private static async Task<IResult> GetByIdAsync(
        Guid id,
        IClassTypeStore store,
        CancellationToken cancellationToken)
    {
        var found = await store.FindAsync(id, cancellationToken);

        return found is null ? Results.NotFound() : Results.Ok(ToDto(found));
    }

    private static async Task<IResult> CreateAsync(
        ClassTypeRequest request,
        IClassTypeStore store,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var invalid = Validate(request);
        if (invalid is not null)
        {
            return invalid;
        }

        var name = request.Name.Trim();

        if (await store.IsNameTakenAsync(name, null, cancellationToken))
        {
            return Results.Json(new ClassTypeFailure("name_taken"), statusCode: 409);
        }

        var created = new ClassType
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = NormalizeDescription(request.Description),
            DefaultDurationMinutes = request.DefaultDurationMinutes,
            DefaultCapacity = request.DefaultCapacity,
            IsActive = true,
            CreatedAt = timeProvider.GetUtcNow(),
        };

        store.Add(created);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Results.Ok(ToDto(created));
    }

    /// <summary>
    /// Replaces every field of a type. Editing the name or description propagates to every
    /// occurrence that references it, past ones included — that is FR-007's identity-by-reference
    /// half, and it is what makes a correction apply everywhere at once.
    ///
    /// The numbers do NOT propagate: they were copied onto each occurrence when it was created, so
    /// changing them here affects only occurrences scheduled from now on. That asymmetry is what
    /// keeps a type edit from moving the capacity the no-overbooking guarantee is checked against.
    /// </summary>
    private static async Task<IResult> UpdateAsync(
        Guid id,
        ClassTypeRequest request,
        IClassTypeStore store,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var existing = await store.FindAsync(id, cancellationToken);
        if (existing is null)
        {
            return Results.NotFound();
        }

        var invalid = Validate(request);
        if (invalid is not null)
        {
            return invalid;
        }

        var name = request.Name.Trim();

        // Excluding its own id, or every edit that keeps the name would collide with itself.
        if (await store.IsNameTakenAsync(name, id, cancellationToken))
        {
            return Results.Json(new ClassTypeFailure("name_taken"), statusCode: 409);
        }

        existing.Name = name;
        existing.Description = NormalizeDescription(request.Description);
        existing.DefaultDurationMinutes = request.DefaultDurationMinutes;
        existing.DefaultCapacity = request.DefaultCapacity;

        // No concurrency token on ClassType, and SaveChangesAsync rather than TrySaveChangesAsync -
        // the same deliberate departure from the MemberAdminEndpoints pattern that ClassStore
        // records: exactly one admin account is ever seeded (AdminSeeder), so there is no second
        // writer to lose a race against. A second admin makes this last-write-wins, at which point
        // ClassType needs a ConcurrencyStamp and these handlers need the 409.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Results.Ok(ToDto(existing));
    }

    /// <summary>
    /// Retires a type: it leaves every selection, and the occurrences referencing it are untouched
    /// (FR-006). No uniqueness check is needed — deactivating only ever RELEASES a name.
    ///
    /// Idempotent. Deactivating an already-inactive type is a 200, not a refusal: nothing is gained
    /// by failing, and the screen would have to explain an error that means "already done".
    /// </summary>
    private static async Task<IResult> DeactivateAsync(
        Guid id,
        IClassTypeStore store,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var existing = await store.FindAsync(id, cancellationToken);
        if (existing is null)
        {
            return Results.NotFound();
        }

        existing.IsActive = false;
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Results.Ok(ToDto(existing));
    }

    /// <summary>
    /// Puts a retired type back into circulation.
    ///
    /// <para>
    /// THE UNIQUENESS CHECK HERE IS NOT DECORATION. Deactivating releases a name, so another type
    /// may have claimed it in the meantime. Reactivating blindly would violate
    /// IX_ClassTypes_Name_Active and surface as an unhandled DbUpdateException — a 500 for what is
    /// really a conflict the admin can resolve. The request carries no name, which is exactly why
    /// this is easy to miss.
    /// </para>
    ///
    /// <para>
    /// Checked unconditionally rather than only when currently inactive: excluding this type's own
    /// id makes the check a no-op for an already-active type (the filtered index guarantees no other
    /// active type holds the name), so idempotency costs nothing and there is one path to reason
    /// about instead of two.
    /// </para>
    /// </summary>
    private static async Task<IResult> ActivateAsync(
        Guid id,
        IClassTypeStore store,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var existing = await store.FindAsync(id, cancellationToken);
        if (existing is null)
        {
            return Results.NotFound();
        }

        if (await store.IsNameTakenAsync(existing.Name, id, cancellationToken))
        {
            return Results.Json(new ClassTypeFailure("name_taken"), statusCode: 409);
        }

        existing.IsActive = true;
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Results.Ok(ToDto(existing));
    }

    /// <summary>
    /// The rules shared by create and edit. Hand-rolled, like every other validation in this
    /// codebase — there is no validation library here and adding one for four fields is not
    /// warranted.
    /// </summary>
    private static IResult? Validate(ClassTypeRequest request)
    {
        // Description is the one genuinely optional field in the scheduling context, so it is
        // absent from this check on purpose.
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.Json(new ClassTypeFailure("missing_field"), statusCode: 400);
        }

        // Measured on the TRIMMED value, which is what gets stored - otherwise trailing whitespace
        // could be refused for a description that fits.
        if (NormalizeDescription(request.Description) is { Length: > MaxDescriptionLength })
        {
            return Results.Json(new ClassTypeFailure("description_too_long"), statusCode: 400);
        }

        if (request.DefaultDurationMinutes is < MinDurationMinutes or > MaxDurationMinutes)
        {
            return Results.Json(new ClassTypeFailure("invalid_duration"), statusCode: 400);
        }

        if (request.DefaultCapacity is < MinCapacity or > MaxCapacity)
        {
            return Results.Json(new ClassTypeFailure("invalid_capacity"), statusCode: 400);
        }

        return null;
    }

    /// <summary>
    /// Trims, and collapses "absent" to a single representation. A whitespace-only description and a
    /// missing one mean the same thing to a reader, so they must not be two different values in the
    /// database — otherwise the screen needs to test for both.
    /// </summary>
    private static string? NormalizeDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    private static ClassTypeSummary ToDto(ClassType entity) =>
        new(entity.Id,
            entity.Name,
            entity.Description,
            entity.DefaultDurationMinutes,
            entity.DefaultCapacity,
            entity.IsActive,
            entity.CreatedAt);
}

/// <summary>
/// Narrow read seam over the class-type table, so Application does not reference EF Core (AGENTS.md
/// layering). Implemented in Infrastructure.
/// </summary>
public interface IClassTypeQuery
{
    /// <summary>Every type, active first and then by name. Unbounded — see GetAllAsync.</summary>
    Task<IReadOnlyList<ClassTypeSummary>> GetAllAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The write counterpart. Intention-revealing methods rather than a generic repository — this
/// codebase has no repository pattern and this slice does not introduce one.
///
/// No Remove. FR-006 rules out hard deletion; deactivation is a field on the entity, not an
/// operation on the store.
///
/// Nothing here saves. The endpoint commits through <see cref="IUnitOfWork"/>.
/// </summary>
public interface IClassTypeStore
{
    Task<ClassType?> FindAsync(Guid id, CancellationToken cancellationToken);

    void Add(ClassType entity);

    /// <summary>
    /// Whether another ACTIVE type already holds <paramref name="name"/>. Inactive types are
    /// invisible here, which is what lets a retired name be reused (FR-006).
    /// </summary>
    /// <param name="excludingId">
    /// The type being edited or activated, so it does not collide with itself. Null when creating.
    /// </param>
    Task<bool> IsNameTakenAsync(string name, Guid? excludingId, CancellationToken cancellationToken);
}
