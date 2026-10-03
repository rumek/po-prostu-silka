using po_prostu_silka.Application.Scheduling;
using po_prostu_silka.Domain;

namespace po_prostu_silka.Api.Endpoints.Scheduling;

/// <summary>
/// A group's fixed roster (S-37), and the trainer's list of groups.
///
/// <para>
/// ------------------------------------------------------------------
/// THE GROUP POLICY ALONE IS NOT SUFFICIENT HERE, AS ON THE STAFF BOOKING ROUTES. READ BEFORE ADDING
/// AN ENDPOINT.
///
/// TrainerOrAdmin admits every trainer; a trainer manages only a group they instruct an upcoming class
/// of. Every roster handler calls <see cref="RosterAuthorization.AuthorizeAsync"/> first, and ANY
/// endpoint added to the roster group must too - a new route inherits the group's admission and none of
/// its narrowing.
/// ------------------------------------------------------------------
/// </para>
/// </summary>
public static class GroupRosterEndpoints
{
    public static IEndpointRouteBuilder MapGroupRosterEndpoints(this IEndpointRouteBuilder app)
    {
        var roster = app.MapGroup("/api/groups/{groupId:guid}/roster")
            .WithTags("Rosters")
            .RequireAuthorization(AuthorizationPolicyNames.TrainerOrAdmin);

        roster.MapGet("/", GetGroupRoster.HandleAsync);
        roster.MapPost("/", AddToRoster.HandleAsync);
        roster.MapDelete("/{memberId:guid}", RemoveFromRoster.HandleAsync);
        roster.MapPost("/sync", SyncRoster.HandleAsync);

        // Narrowed in the handler to the caller's own groups, the way the instructed-classes feed is.
        app.MapGet("/api/trainer/groups", GetTrainerGroups.HandleAsync)
            .WithTags("Rosters")
            .RequireAuthorization(AuthorizationPolicyNames.TrainerOrAdmin);

        return app;
    }
}
