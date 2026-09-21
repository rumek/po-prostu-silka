namespace po_prostu_silka.Application.Training;

/// <summary>
/// A member and their active plan, addressed by the MEMBER (S-22) — what the plan screen loads when it
/// is reached through Członkowie rather than through a plan list.
///
/// <para>
/// 404 ONLY FOR A MEMBER WHO DOES NOT EXIST. A member with no plan is 200 with a null plan: that is
/// the ordinary state the screen answers with an empty builder, and it must not look like a failure.
/// </para>
///
/// <para>
/// STATUS IS NOT FILTERED. An admin must reach a blocked member's plan to read or edit it, and the
/// write side still refuses to CREATE one for them (<c>member_not_active</c>). A trainer who reaches a
/// blocked member by typing an id learns only a name — the same trainer-safe identity the list shows.
/// </para>
///
/// <para>
/// READS MOVE TO THE MEMBER, WRITES STAY BY PLAN ID. <c>PUT /api/trainer/plans/{id}</c> keeps
/// protecting a stale tab: a plan replaced underneath it is archived, and the PUT answers 404 rather
/// than overwriting the new one.
/// </para>
/// </summary>
public static class GetMemberPlan
{
    public static async Task<IResult> HandleAsync(
        Guid memberId,
        ITrainingPlanQuery query,
        CancellationToken cancellationToken)
    {
        var member = await query.FindMemberAsync(memberId, cancellationToken);
        if (member is null)
        {
            return Results.NotFound();
        }

        var plan = await query.FindActiveForMemberAsync(memberId, cancellationToken);

        return Results.Ok(new MemberPlan(member, plan));
    }
}
