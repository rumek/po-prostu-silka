using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Application.Scheduling;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// Creates a member record for a person who has no account (AM-001).
/// </summary>
public static class CreateMember
{
    /// <summary>
    /// Records a person who has no account (S-14, AM-001).
    ///
    /// Creates a member and NOTHING ELSE — no Identity row, no password, no invitation. The person
    /// gets a login only if they later register with an access code, and until then they exist purely
    /// as the club's own record.
    /// </summary>
    public static async Task<IResult> HandleAsync(
        [FromBody] MemberRequest request,
        IMemberStore members,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!MemberRequestReader.TryRead(request, out var displayName, out var contact, out var failure))
        {
            return failure;
        }

        var member = new Member
        {
            Id = Guid.NewGuid(),
            UserId = null,
            DisplayName = displayName,

            // NULL, ALWAYS (S-17). The desk does not take an address; the one this row will hold is
            // the login the member registers with, written by RegisterAsync. That also removes the
            // pre-insert uniqueness check that used to guard IX_Members_Email here — nothing this
            // endpoint writes can collide with it any more.
            Email = null,
            PhoneNumber = contact?.PhoneNumber,
            Street = contact?.Street,
            HouseNumber = contact?.HouseNumber,
            PostalCode = contact?.PostalCode,
            City = contact?.City,

            // Active on creation: an admin typing someone in has vetted them by the act of typing them
            // in. See MembershipStatus for why there is no membership Pending to land in instead.
            Status = MembershipStatus.Active,
            CreatedAt = timeProvider.GetUtcNow(),
        };

        members.Add(member);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/admin/members/{member.Id}", new { id = member.Id });
    }
}
