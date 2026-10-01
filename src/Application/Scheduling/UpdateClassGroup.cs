using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// Edits a group (FR-006).
/// </summary>
public static class UpdateClassGroup
{
    /// <summary>
    /// Replaces every field of a group. Editing the name or description propagates to every
    /// occurrence that references it, past ones included — that is FR-007's identity-by-reference
    /// half, and it is what makes a correction apply everywhere at once.
    ///
    /// The numbers do NOT propagate: they were copied onto each occurrence when it was created, so
    /// changing them here affects only occurrences scheduled from now on. That asymmetry is what
    /// keeps a group edit from moving the capacity the no-overbooking guarantee is checked against.
    /// </summary>
    public static async Task<IResult> HandleAsync(
        Guid id,
        ClassGroupRequest request,
        IClassGroupStore store,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var existing = await store.FindAsync(id, cancellationToken);
        if (existing is null)
        {
            return Results.NotFound();
        }

        var invalid = ClassGroupValidator.Validate(request);
        if (invalid is not null)
        {
            return invalid;
        }

        var name = request.Name.Trim();

        // Excluding its own id, or every edit that keeps the name would collide with itself.
        if (await store.IsNameTakenAsync(name, id, cancellationToken))
        {
            return Results.Json(new ClassGroupFailure("name_taken"), statusCode: 409);
        }

        existing.Name = name;
        existing.Description = ClassGroupValidator.NormalizeDescription(request.Description);
        existing.DefaultDurationMinutes = request.DefaultDurationMinutes;
        existing.DefaultCapacity = request.DefaultCapacity;

        // No concurrency token on ClassGroup, and SaveChangesAsync rather than TrySaveChangesAsync -
        // the same deliberate departure from the MemberAdminEndpoints pattern that ClassStore
        // records: exactly one admin account is ever seeded (AdminSeeder), so there is no second
        // writer to lose a race against. A second admin makes this last-write-wins, at which point
        // ClassGroup needs a ConcurrencyStamp and these handlers need the 409.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Results.Ok(ClassGroupProjection.ToDto(existing));
    }
}
