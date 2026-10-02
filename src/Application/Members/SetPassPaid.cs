using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

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
/// about access: settling a debt after the account was blocked is an ordinary thing to record. A STAFF
/// HOLDER'S KARNET IS A 404, because staff hold no member data (S-25). It is a 404 rather than the issue
/// path's 409 <c>member_is_staff</c> so that, as with the trainer's read of a member's karnets, staff
/// "do not exist" from a trainer screen, and a pass id cannot be probed for whose it is.
/// </para>
///
/// <para>
/// NO STAMP IS ROTATED OR CHECKED. <c>MembershipPass.ConcurrencyStamp</c> guards the entry pool, which
/// payment does not touch. A tracked save would still put the stamp in its WHERE clause and lose to a
/// booking landing at the same moment, so the write goes through
/// <see cref="IMembershipPassStore.SetPaymentAsync"/>, which updates the two payment columns alone.
/// Concurrent payment writes are last-writer-wins, and since the value is explicit they converge.
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
            return Results.NotFound();
        }

        // Written even when the value does not change: the request is an assertion, and the recorder is
        // whoever made the latest one. Clearing records its author too, so an undo is attributable.
        var recordedBy = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!await passes.SetPaymentAsync(passId, request.PaidAt, recordedBy, cancellationToken))
        {
            // Revoked between the read above and the write.
            return Results.NotFound();
        }

        // Mirrors the committed values onto the instance for the response only. Nothing in this request
        // saves the unit of work, so the tracked entity is never written back.
        pass.PaidAt = request.PaidAt;
        pass.PaidRecordedBy = recordedBy;

        var used = await MembershipPassProjection.EntriesUsedAsync(query, pass.MemberId, passId, cancellationToken);

        return Results.Ok(await MembershipPassProjection.ViewOfAsync(pass, used, timeProvider));
    }
}
