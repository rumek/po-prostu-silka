using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// Defines a class type (FR-005).
/// </summary>
public static class CreateClassType
{
    public static async Task<IResult> HandleAsync(
        ClassTypeRequest request,
        IClassTypeStore store,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var invalid = ClassTypeValidator.Validate(request);
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
            Description = ClassTypeValidator.NormalizeDescription(request.Description),
            DefaultDurationMinutes = request.DefaultDurationMinutes,
            DefaultCapacity = request.DefaultCapacity,
            IsActive = true,
            CreatedAt = timeProvider.GetUtcNow(),
        };

        store.Add(created);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Results.Ok(ClassTypeProjection.ToDto(created));
    }
}
