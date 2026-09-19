using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// Copies an occurrence forward over a bounded number of weeks (FR-012).
/// </summary>
public static class DuplicateClass
{
    /// <summary>Bounds on a duplicate batch. Above this it is a recurring series, which the PRD parks.</summary>
    private const int MaxDuplicateWeeks = 8;

    /// <summary>
    /// Copies a class into the next N weeks (prd-v2 FR-013) — the deliberate MVP substitute for
    /// recurring series.
    ///
    /// PARTIAL SUCCESS IS THE POINT. A week whose time is already taken is skipped and reported, not
    /// fatal: one clash in week seven must not throw away six good copies and send the admin hunting
    /// for it. Every surviving copy lands in ONE save, so a batch that fails to commit leaves no
    /// half-created weeks behind.
    ///
    /// <para>
    /// The copies carry the SOURCE's type, instructor and numbers verbatim. No validation of the
    /// type's active state runs here: the source occurrence is already valid, and refusing to
    /// duplicate a class because its type was retired afterwards would contradict FR-006 exactly as
    /// refusing to edit it would.
    /// </para>
    /// </summary>
    public static async Task<IResult> HandleAsync(
        Guid id,
        DuplicateRequest request,
        IClassStore store,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var source = await store.FindAsync(id, cancellationToken);
        if (source is null)
        {
            return Results.NotFound();
        }

        if (request.Weeks < 1 || request.Weeks > MaxDuplicateWeeks)
        {
            return Results.Json(new ClassFailure("invalid_weeks"), statusCode: 400);
        }

        var now = timeProvider.GetUtcNow();
        var skipped = new List<int>();
        var created = 0;

        for (var week = 1; week <= request.Weeks; week++)
        {
            // Seven days added in the CLUB's local time, not on the UTC instant.
            //
            // DateTimeOffset.AddDays would preserve the instant, which is not what a timetable
            // means: a 22:34 class duplicated across the October DST transition would silently land
            // at 21:34: right instant, wrong wall clock, and nothing would fail. Members read a wall
            // clock, so the wall clock is what has to survive. See ClubTime.
            var startsAt = ClubTime.AddLocalDays(source.StartsAt, 7 * week);

            // Each copy is checked independently, against rows already in the database AND against
            // the copies queued earlier in this same batch — otherwise two weeks of a batch could
            // collide with each other and both be written.
            if (await store.HasTimeConflictAsync(
                    startsAt, source.DurationMinutes, null, cancellationToken))
            {
                skipped.Add(week);
                continue;
            }

            store.Add(new Class
            {
                Id = Guid.NewGuid(),
                ClassTypeId = source.ClassTypeId,
                StartsAt = startsAt,
                DurationMinutes = source.DurationMinutes,
                InstructorMemberId = source.InstructorMemberId,
                Capacity = source.Capacity,
                Status = ClassStatus.Scheduled,
                CreatedAt = now,
            });

            created++;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Results.Ok(new DuplicateResult(created, skipped));
    }
}
