namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// A refused roster write (S-37), as a 409: <c>already_in_roster</c>, <c>roster_full</c>,
/// <c>member_blocked</c>, <c>member_is_staff</c>, <c>inactive_class_group</c>, <c>conflict</c>.
/// </summary>
public record RosterFailure(string Reason)
{
    public static IResult Refuse(string reason) => Results.Json(new RosterFailure(reason), statusCode: 409);
}

/// <summary>The body of <c>POST /api/groups/{groupId}/roster</c>.</summary>
public record AddToRosterRequest(Guid MemberId);
