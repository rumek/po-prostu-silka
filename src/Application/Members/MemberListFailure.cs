namespace po_prostu_silka.Application.Members;

/// <summary>
/// Why a member-list read was refused (S-21). <c>invalid_page</c> — a page below 1, or a page size
/// outside 1–<see cref="GetMembers.MaxPageSize"/>; <c>invalid_search</c> — a phrase longer than
/// <see cref="GetMembers.MaxSearchLength"/>.
///
/// <para>
/// A 400 rather than a clamp, for the reason <c>ClassRangeResolver</c> refuses a partial range: the
/// SPA never sends these values, so a caller that does has a bug, and quietly answering a different
/// question than the one asked would hide it.
/// </para>
/// </summary>
public record MemberListFailure(string Reason);
