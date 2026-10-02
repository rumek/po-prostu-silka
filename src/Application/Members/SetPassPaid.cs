using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Persistence;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// Marks a karnet paid or unpaid (pass-paid-flag) — the ONLY write path for payment on an existing
/// karnet, shared by the admin's karnet screen and the trainer's.
///
/// <para>
/// TRAINER OR ADMIN. A trainer may take cash at the gym, so they may record it for any non-staff
/// member — the same reach <c>/api/trainer/members</c> already has. This is deliberately a narrow write
/// beside the admin's full edit, which a trainer must not inherit (S-16, MP-04..06).
/// </para>
///
/// <para>
/// A BLOCKED MEMBER IS ALLOWED, unlike <see cref="IssuePass"/>. Payment is a fact about money, not
/// about access: settling a debt after the account was blocked is an ordinary thing to record. STAFF
/// ARE REFUSED (<c>member_is_staff</c>), because staff hold no member data (S-25) — the same rule the
/// issue path applies.
/// </para>
///
/// <para>
/// NO STAMP IS ROTATED. <c>MembershipPass.ConcurrencyStamp</c> guards the entry pool, which payment does
/// not touch; rotating it would make this write race a concurrent booking for nothing. Concurrent
/// payment writes are last-writer-wins, and since the value is explicit they converge.
/// </para>
/// </summary>
public static class SetPassPaid
{
    public static async Task<IResult> HandleAsync(
        Guid passId,
        [FromBody] PassPaidRequest request,
        IMemberStore members,
        IMembershipPassStore passes,
        IMembershipPassQuery query,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var pass = await passes.FindAsync(passId, cancellationToken);
        if (pass is null)
        {
            return Results.NotFound();
        }

        if (!MembershipPassProjection.TryReadPaidAt(request.PaidAt, timeProvider, out var failure))
        {
            return failure;
        }

        if (await members.IsStaffAsync(pass.MemberId, cancellationToken))
        {
            return Results.Json(new MembershipPassFailure("member_is_staff"), statusCode: 409);
        }

        // Written even when the value does not change: the request is an assertion, and the recorder is
        // whoever made the latest one. Clearing records its author too, so an undo is attributable.
        pass.PaidAt = request.PaidAt;
        pass.PaidRecordedBy = principal.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            return Results.Json(new MembershipPassFailure("conflict"), statusCode: 409);
        }

        var used = await MembershipPassProjection.EntriesUsedAsync(query, pass.MemberId, passId, cancellationToken);

        return Results.Ok(await MembershipPassProjection.ViewOfAsync(pass, used, timeProvider));
    }
}
