using po_prostu_silka.Application.Members;

namespace po_prostu_silka.Application.Training;

/// <summary>
/// The trainer's member list (S-22, UX-08): one page of the members a plan may be given to, searched
/// by name, each row saying whether the member already has a plan.
///
/// <para>
/// THE BOUNDS ARE THE ADMIN LIST'S, through <see cref="MemberListRequest"/>, so both lists refuse the
/// same input with the same <see cref="MemberListFailure"/> reasons and the SPA's one table answers
/// for both.
/// </para>
/// </summary>
public static class GetTrainerMembers
{
    public static async Task<IResult> HandleAsync(
        string? search,
        int? page,
        int? pageSize,
        ITrainingPlanQuery query,
        CancellationToken cancellationToken)
    {
        if (!MemberListRequest.TryRead(page, pageSize, search, out var request, out var refusal))
        {
            return refusal;
        }

        return Results.Ok(await query.GetTrainerMembersAsync(
            request.Term, request.Page, request.PageSize, cancellationToken));
    }
}
