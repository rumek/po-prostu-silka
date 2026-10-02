using po_prostu_silka.Application.Members;

namespace po_prostu_silka.Application.Training;

/// <summary>
/// One member's karnets for the trainer, newest first (pass-paid-flag) — what lets a trainer settle an
/// unpaid karnet that is no longer the one covering today.
///
/// <para>
/// 404 FOR AN UNKNOWN MEMBER AND FOR STAFF. Staff hold no karnet (S-25), so from a trainer screen they
/// do not exist — the same answer the plan screen gives. A blocked member is returned: payment may be
/// recorded for them (see <see cref="SetPassPaid"/>), though the trainer's list never offers them.
/// </para>
/// </summary>
public static class GetTrainerMemberPasses
{
    public static async Task<IResult> HandleAsync(
        Guid memberId,
        IMemberStore members,
        IMembershipPassQuery passes,
        CancellationToken cancellationToken)
    {
        var member = await members.FindAsync(memberId, cancellationToken);
        if (member is null || await members.IsStaffAsync(memberId, cancellationToken))
        {
            return Results.NotFound();
        }

        var history = await passes.GetForMemberAsync(memberId, cancellationToken);

        return Results.Ok(new TrainerMemberPasses(member.Id, member.DisplayName, history));
    }
}
