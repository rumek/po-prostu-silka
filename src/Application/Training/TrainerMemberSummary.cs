namespace po_prostu_silka.Application.Training;

/// <summary>
/// One row of the trainer's member list (S-22, UX-08) — the way a trainer reaches a member's plan.
///
/// <para>
/// THESE FOUR FIELDS ARE DELIBERATELY ALL A TRAINER GETS. No e-mail, no phone, no status, no account
/// id: prd.md's privacy NFR keeps member data between the admin and the member, and the trainer
/// surface has only ever been widened by a minimized projection — <see cref="AssignableMember"/> is
/// the precedent this follows. <c>TrainerMemberEndpointTests</c> pins the absence of the e-mail on
/// the raw response body, because a record that grew a field would pass every typed assertion.
/// </para>
/// </summary>
/// <param name="Id">The MEMBER's id — what the plan screen's URL carries.</param>
/// <param name="HasAccount">Whether they can sign in, and so whether they will ever see the plan.</param>
/// <param name="PlanName">The ACTIVE plan's name, or null when the member has none. Archived plans
/// never count.</param>
public record TrainerMemberSummary(Guid Id, string DisplayName, bool HasAccount, string? PlanName);
