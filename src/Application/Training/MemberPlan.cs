namespace po_prostu_silka.Application.Training;

/// <summary>
/// A member and their active plan — what the plan screen loads when it is reached through the member
/// (S-22, UX-07/UX-08), for an admin and a trainer alike.
///
/// <para>
/// THE MEMBER HALF IS <see cref="AssignableMember"/>, not the admin's member detail, and that is the
/// point: the same payload answers a trainer, so it carries nothing prd.md's privacy NFR keeps from
/// one — no e-mail, no phone, no status.
/// </para>
/// </summary>
/// <param name="Plan">The member's active plan, or null when they have none — an ordinary state, which
/// the screen answers with an empty builder rather than an error.</param>
public record MemberPlan(AssignableMember Member, TrainingPlanDetail? Plan);
