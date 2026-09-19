using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// Issues a karnet to a member (MP-01).
/// </summary>
public static class IssuePass
{
    /// <summary>
    /// Issues a karnet (MP-04).
    ///
    /// <para>
    /// The insert and the rotation of <see cref="Member.ConcurrencyStamp"/> land in ONE
    /// <c>SaveChangesAsync</c>. That pairing is the whole non-overlap guarantee — see the type's
    /// remarks. Removing the rotation would leave a probe that is right most of the time.
    /// </para>
    /// </summary>
    public static async Task<IResult> HandleAsync(
        Guid memberId,
        [FromBody] IssuePassRequest request,
        IMemberStore members,
        IMembershipPassStore passes,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var member = await members.FindAsync(memberId, cancellationToken);
        if (member is null)
        {
            return Results.NotFound();
        }

        if (!MembershipPassProjection.TryRead(request, out var typeName, out var failure))
        {
            return failure;
        }

        // THE SCREEN IS NOT THE BOUNDARY, the same rule IssueAccessCodeAsync states. A pass issued to
        // a blocked member entitles them to nothing — every booking would be refused by the
        // membership check before the pass is even consulted — so handing the admin a receipt for it
        // would be a lie about what the club just sold.
        if (member.Status != MembershipStatus.Active)
        {
            return Results.Json(new MembershipPassFailure("member_blocked"), statusCode: 409);
        }

        var overlapping = await passes.FindOverlappingAsync(
            memberId,
            request.ValidFrom,
            request.ValidTo,
            excludingPassId: null,
            cancellationToken);

        if (overlapping is not null)
        {
            return Results.Json(new MembershipPassFailure("overlapping_pass"), statusCode: 409);
        }

        var pass = new MembershipPass
        {
            Id = Guid.NewGuid(),
            MemberId = memberId,
            TypeName = typeName,
            ValidFrom = request.ValidFrom,
            ValidTo = request.ValidTo,
            EntryCount = request.EntryCount,
            IssuedAt = timeProvider.GetUtcNow(),
        };

        passes.Add(pass);

        // The rotation that makes the probe above an invariant. It has nothing to do with the member
        // row's own contents — it is a lock taken on the member for the duration of this write.
        member.ConcurrencyStamp = Guid.NewGuid().ToString();

        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            return Results.Json(new MembershipPassFailure("conflict"), statusCode: 409);
        }

        // Freshly issued, so nothing can have consumed an entry yet — but read it back through the
        // query rather than constructing the view here, so the screen's first render comes from the
        // same projection every later refresh uses and CoversToday cannot disagree between them.
        var view = await MembershipPassProjection.ViewOfAsync(pass, 0, timeProvider);

        return Results.Created($"/api/admin/members/{memberId}/passes/{pass.Id}", view);
    }
}
