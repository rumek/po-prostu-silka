namespace po_prostu_silka.Application.Training;

/// <summary>
/// Whether a member may be given a training plan, and if not, why (S-25). Replaced a <c>bool?</c>
/// once there were three ways to say no: each maps to its own refusal in
/// <see cref="TrainingPlanValidator.ValidateMemberAsync"/>.
/// </summary>
public enum MemberAssignability
{
    /// <summary>No such member — <c>member_not_found</c>.</summary>
    NotFound,

    /// <summary>The membership, or the linked account, is not active — <c>member_not_active</c>.</summary>
    NotActive,

    /// <summary>The member's account holds Trainer or Admin — <c>member_is_staff</c>.</summary>
    Staff,

    /// <summary>May be assigned a plan; exactly the members the trainer's list offers.</summary>
    Assignable,
}
