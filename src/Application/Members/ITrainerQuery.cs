using po_prostu_silka.Domain;

namespace po_prostu_silka.Application.Members;

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
