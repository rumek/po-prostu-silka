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
///
/// <para>
/// <see cref="PaidAt"/> IS READ BY THE POST ONLY (pass-paid-flag). <see cref="UpdatePass"/> ignores it:
/// a payment on an existing karnet changes only through <c>SetPassPaid</c>. Otherwise every
/// client that edits with the four original fields — the E2E helper, the tests, an older SPA — would
/// silently wipe a recorded payment. Optional and last, so those callers stay valid, and absent means
/// unpaid, which is the issue form's default.
/// </para>
/// </summary>
public record IssuePassRequest(
    string TypeName,
    DateOnly ValidFrom,
    DateOnly ValidTo,
    int EntryCount,
    DateOnly? PaidAt = null);
