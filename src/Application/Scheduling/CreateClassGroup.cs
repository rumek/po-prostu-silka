using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// Defines a group (FR-005).
/// </summary>
public static class CreateClassGroup
{
    public static async Task<IResult> HandleAsync(
        ClassGroupRequest request,
        IClassGroupStore store,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var invalid = ClassGroupValidator.Validate(request);
        if (invalid is not null)
        {
            return invalid;
        }

        var name = request.Name.Trim();

        if (await store.IsNameTakenAsync(name, null, cancellationToken))
        {
            return Results.Json(new ClassGroupFailure("name_taken"), statusCode: 409);
        }

        var created = new ClassGroup
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = ClassGroupValidator.NormalizeDescription(request.Description),
            DefaultDurationMinutes = request.DefaultDurationMinutes,
            DefaultCapacity = request.DefaultCapacity,
            IsActive = true,
            CreatedAt = timeProvider.GetUtcNow(),
        };

        store.Add(created);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Results.Ok(ClassGroupProjection.ToDto(created));
    }
}
