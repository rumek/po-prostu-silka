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
/// <param name="Id">
/// The MEMBER's id (S-14). Everything the SPA submits speaks member ids; the server resolves the
/// account behind it when it validates the assignment.
/// </param>
public record TrainerSummary(Guid Id, string DisplayName);
