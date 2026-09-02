using po_prostu_silka.Domain;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// A trainer as the class form's instructor selection sees them (prd-v2 FR-009). This is a CONTRACT
/// the SPA's member-admin service mirrors — renaming a field breaks the class form silently.
///
/// <para>
/// TWO FIELDS, DELIBERATELY. <see cref="MemberSummary"/> already describes an account far more fully,
/// and reusing it here would ship every trainer's email address and account status into a dropdown
/// that needs neither. What a selection needs is the value it submits and the label it shows.
/// </para>
/// </summary>
public record TrainerSummary(string Id, string DisplayName);

/// <summary>
/// The people an occurrence may name as its instructor (prd-v2 FR-009).
///
/// <para>
/// A SEPARATE GROUP FROM MemberAdminEndpoints, on the same policy. The two are about the same table
/// but answer different questions: that one manages accounts, this one populates a selection in the
/// scheduling context. Keeping them apart is what lets S-07 consume this without inheriting the
/// member list's shape.
/// </para>
///
/// <para>
/// Admin-only, with the policy applied at the GROUP rather than per endpoint, so an endpoint added
/// here later cannot accidentally ship unauthenticated. Members never read this: a trainer reaches
/// them as a resolved display name on the schedule, never as a list to choose from.
/// </para>
///
/// <para>
/// There is no write surface here. Granting and revoking the Trainer role stays on
/// <see cref="MemberAdminEndpoints"/>, where the account lives; this slice consumes the role and
/// does not change how it is given.
/// </para>
/// </summary>
public static class TrainerEndpoints
{
    public static IEndpointRouteBuilder MapTrainerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/trainers")
            .WithTags("Members")
            .RequireAuthorization(AuthorizationPolicyNames.Admin);

        group.MapGet("/", GetTrainersAsync);

        return app;
    }

    /// <summary>
    /// Every ACTIVE account holding the Trainer role, by display name.
    ///
    /// <para>
    /// The active filter is not cosmetic: it is the read-side half of the rule
    /// <c>ClassEndpoints</c> enforces on write. A blocked trainer must not be offered in the
    /// selection, or the admin picks a name the server then refuses.
    /// </para>
    ///
    /// <para>
    /// No pagination and no filter parameter — a single club's trainer list is a handful of rows, and
    /// this endpoint exists to fill one dropdown.
    /// </para>
    /// </summary>
    private static async Task<IResult> GetTrainersAsync(
        ITrainerQuery query,
        CancellationToken cancellationToken) =>
        Results.Ok(await query.GetActiveTrainersAsync(cancellationToken));
}

/// <summary>
/// Narrow read seam over the user table filtered by role, so Application does not reference EF Core
/// (AGENTS.md layering). Implemented in Infrastructure.
///
/// <para>
/// Separate from <see cref="IMemberQuery"/> rather than a parameter on it: that one browses accounts
/// with their statuses and roles for the admin's management screen, this one answers "who may run a
/// class". Folding them together would grow the member list's DTO with a field its own screen never
/// reads.
/// </para>
/// </summary>
public interface ITrainerQuery
{
    /// <summary>Active accounts holding the Trainer role, ordered by display name.</summary>
    Task<IReadOnlyList<TrainerSummary>> GetActiveTrainersAsync(CancellationToken cancellationToken);
}
