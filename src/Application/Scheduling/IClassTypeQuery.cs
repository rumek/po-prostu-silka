using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// Narrow read seam over the class-type table, so Application does not reference EF Core (AGENTS.md
/// layering). Implemented in Infrastructure.
/// </summary>
public interface IClassTypeQuery
{
    /// <summary>Every type, active first and then by name. Unbounded — see GetAllAsync.</summary>
    Task<IReadOnlyList<ClassTypeSummary>> GetAllAsync(CancellationToken cancellationToken);
}
