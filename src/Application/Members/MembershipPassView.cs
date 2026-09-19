using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// One karnet as the admin's screen sees it. This is a CONTRACT the SPA's member-admin service
/// mirrors — renaming a field breaks the pass screen silently.
///
/// <para>
/// <see cref="EntriesUsed"/> AND <see cref="EntriesLeft"/> ARE DERIVED, NOT STORED. Used is the count
/// of active bookings carrying this pass's id; left is <see cref="EntryCount"/> minus that. See
/// <see cref="MembershipPass"/> for why there is no counter column behind them — and note the
/// consequence for this record: these two fields are a reading taken at query time, not a fact about
/// the row, so a screen holding them is holding a snapshot and must refetch after any booking action.
/// </para>
///
/// <para>
/// The validity range crosses the wire as <see cref="DateOnly"/>, which System.Text.Json renders as
/// <c>"2026-09-30"</c> — a DAY, with no hour and no offset to be misread in another timezone. Every
/// other timestamp in this app is a UTC instant; this one deliberately is not, because a karnet valid
/// "to the 30th" means the whole 30th wherever the reader is standing.
/// </para>
/// </summary>
public record MembershipPassView(
    Guid Id,
    string TypeName,
    DateOnly ValidFrom,
    DateOnly ValidTo,
    int EntryCount,
    int EntriesUsed,
    int EntriesLeft,
    DateTimeOffset IssuedAt,
    bool CoversToday);
