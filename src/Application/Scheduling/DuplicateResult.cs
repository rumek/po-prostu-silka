using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// What a duplicate actually did. NOT a bare success: a batch where some weeks collided is a partial
/// success, and reporting it as "done" would leave the admin believing in classes that were never
/// created.
/// </summary>
/// <param name="Created">How many copies were written.</param>
/// <param name="SkippedWeeks">
/// 1-based week offsets refused because another class already occupies that time. The REASON changed
/// in S-06 — it used to be a room collision — but the shape and the partial-success behaviour did
/// not (prd-v2 FR-013).
/// </param>
public record DuplicateResult(int Created, IReadOnlyList<int> SkippedWeeks);
