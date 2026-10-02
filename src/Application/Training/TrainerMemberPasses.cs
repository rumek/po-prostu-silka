using po_prostu_silka.Application.Members;

namespace po_prostu_silka.Application.Training;

/// <summary>
/// A member's karnet history as the trainer's "Karnety" screen reads it (pass-paid-flag).
///
/// <para>
/// THE NAME AND THE KARNETS, NOTHING ELSE — the same minimized-projection rule as
/// <see cref="TrainerMemberSummary"/>: no e-mail, no phone, no status. The karnets are the admin's
/// <see cref="MembershipPassView"/> unchanged, because a karnet carries no contact data and one view
/// keeps both screens reading the same payment state.
/// </para>
/// </summary>
public record TrainerMemberPasses(Guid MemberId, string DisplayName, IReadOnlyList<MembershipPassView> Passes);
