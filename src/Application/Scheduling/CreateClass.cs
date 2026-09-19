using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// Schedules an occurrence from a class type and a trainer (FR-008..FR-013).
/// </summary>
public static class CreateClass
{
    public static async Task<IResult> HandleAsync(
        ClassRequest request,
        IClassStore store,
        IClassTypeStore classTypes,
        UserManager<ApplicationUser> userManager,
        IMemberStore members,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        var invalid = ClassRequestValidator.Validate(request);
        if (invalid is not null)
        {
            return invalid;
        }

        // Create-only. An admin must be able to correct a class that has already started, so
        // UpdateAsync deliberately does NOT apply this rule.
        if (request.StartsAt <= now)
        {
            return Results.Json(new ClassFailure("starts_in_past"), statusCode: 400);
        }

        var classType = await classTypes.FindAsync(request.ClassTypeId, cancellationToken);
        if (classType is null)
        {
            return Results.Json(new ClassFailure("unknown_class_type"), statusCode: 400);
        }

        // Active is checked HERE ONLY, not on edit. FR-006 promises that deactivating a type leaves
        // its existing occurrences intact - and an occurrence the admin cannot reschedule is not
        // intact. Since the type is immutable after creation (see UpdateAsync), create is the only
        // place a deactivated type could be newly attached to anything.
        if (!classType.IsActive)
        {
            return Results.Json(new ClassFailure("inactive_class_type"), statusCode: 400);
        }

        var (instructorFailure, instructor) =
            await ClassRequestValidator.ValidateInstructorAsync(
                request.InstructorMemberId, userManager, members, cancellationToken);
        if (instructorFailure is not null)
        {
            return instructorFailure;
        }

        if (await store.HasTimeConflictAsync(
                request.StartsAt, request.DurationMinutes, null, cancellationToken))
        {
            return Results.Json(new ClassFailure("time_conflict"), statusCode: 409);
        }

        var created = new Class
        {
            Id = Guid.NewGuid(),
            ClassTypeId = classType.Id,
            StartsAt = request.StartsAt,

            // FROM THE REQUEST, NOT FROM classType (prd-v2 FR-007). The client prefilled these from
            // the type's defaults and the admin may have overridden them; reading
            // classType.DefaultCapacity here instead would both ignore the override and re-open the
            // door to a type edit moving a booked class's capacity. classType is a VALIDATION result
            // on this path, nothing more.
            DurationMinutes = request.DurationMinutes,
            Capacity = request.Capacity,

            InstructorMemberId = request.InstructorMemberId,
            Status = ClassStatus.Scheduled,
            CreatedAt = now,
        };

        store.Add(created);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Projected from what this handler already holds, NOT from a re-read.
        //
        // The freshly-constructed entity's navigations are null, so ToDto cannot take it alone - but
        // classType and instructor are both in hand from the validation above. Re-reading here used
        // to mean a second round-trip AND, when it came back null, a 404 for a row that had just been
        // committed: the client was told the write failed after it succeeded, and an admin retrying a
        // create would produce a duplicate class.
        // Zero bookings, by construction: the occurrence was created this instant, and there is no
        // route by which anything could have booked it before the response is written.
        return Results.Ok(ClassDtoMapping.ToDto(created, classType, instructor!.DisplayName, bookedCount: 0));
    }
}
