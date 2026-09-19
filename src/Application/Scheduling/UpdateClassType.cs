using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// Edits a class type (FR-006).
/// </summary>
public static class UpdateClassType
{
    /// <summary>
    /// Replaces every field of a type. Editing the name or description propagates to every
    /// occurrence that references it, past ones included — that is FR-007's identity-by-reference
    /// half, and it is what makes a correction apply everywhere at once.
    ///
    /// The numbers do NOT propagate: they were copied onto each occurrence when it was created, so
    /// changing them here affects only occurrences scheduled from now on. That asymmetry is what
    /// keeps a type edit from moving the capacity the no-overbooking guarantee is checked against.
    /// </summary>
    public static async Task<IResult> HandleAsync(
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

        var invalid = ClassTypeValidator.Validate(request);
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
        existing.Description = ClassTypeValidator.NormalizeDescription(request.Description);
        existing.DefaultDurationMinutes = request.DefaultDurationMinutes;
        existing.DefaultCapacity = request.DefaultCapacity;

        // No concurrency token on ClassType, and SaveChangesAsync rather than TrySaveChangesAsync -
        // the same deliberate departure from the MemberAdminEndpoints pattern that ClassStore
        // records: exactly one admin account is ever seeded (AdminSeeder), so there is no second
        // writer to lose a race against. A second admin makes this last-write-wins, at which point
        // ClassType needs a ConcurrencyStamp and these handlers need the 409.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Results.Ok(ClassTypeProjection.ToDto(existing));
    }
}
