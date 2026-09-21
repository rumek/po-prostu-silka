namespace po_prostu_silka.Application.Paging;

/// <summary>
/// One page of a list, and how long the whole list is (S-21).
///
/// <para>
/// GENERIC ON PURPOSE. The member list is the first list to page, not the last: the exercise,
/// class-type and plan lists share its design and were deferred only because they grow slower. When
/// they page, they reuse this shape rather than inventing a second one the SPA has to mirror.
/// </para>
///
/// <para>
/// A CONTRACT the SPA mirrors field for field (<c>MemberPage</c> in <c>member-admin.models.ts</c>).
/// <see cref="Page"/> is 1-based, and a page past the end is an empty <see cref="Items"/> with the
/// true <see cref="Total"/> — which is what lets the client tell "this page no longer exists" from
/// "nothing matches".
/// </para>
/// </summary>
public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
