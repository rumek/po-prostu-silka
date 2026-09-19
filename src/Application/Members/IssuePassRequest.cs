using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// What the admin submits to issue or edit a karnet.
///
/// <para>
/// <see cref="EntryCount"/> is NOT nullable and has no "unlimited" spelling. MP-04 settles that a pass
/// always carries a count, and representing unlimited as null here would put a second gate shape into
/// the booking path for a product the club does not sell.
/// </para>
/// </summary>
public record IssuePassRequest(
    string TypeName,
    DateOnly ValidFrom,
    DateOnly ValidTo,
    int EntryCount);
