using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Training;

namespace po_prostu_silka.Application.Training;

/// <summary>
/// The exercise library, for the admin list and the plan builder (FR-018).
/// </summary>
public static class GetExercises
{
    /// <summary>
    /// Every exercise, active and inactive, active first and then by name.
    ///
    /// UNFILTERED, deliberately — same reasoning as the class-type list: the screen's "pokaż
    /// nieaktywne" toggle filters rows it already holds. It is also what makes the form's
    /// muscle-group and difficulty suggestions free: the form reuses this call and derives the
    /// distinct values client-side, so no second endpoint exists to keep in step.
    /// </summary>
    public static async Task<IResult> HandleAsync(
        IExerciseQuery query,
        CancellationToken cancellationToken) =>
        Results.Ok(await query.GetAllAsync(cancellationToken));
}
