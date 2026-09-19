using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Application.Scheduling;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// Edits a member record (AM-002).
/// </summary>
public static class UpdateMember
{
    /// <summary>
    /// Edits a member's own details (S-14, AM-002).
    ///
    /// <para>
    /// APPLIES TO MEMBERS WITH ACCOUNTS TOO, and that is a real widening: before this the admin could
    /// change nobody's details, only their status and roles. It follows from the record being the
    /// club's rather than the account holder's — the same reasoning that makes the display name
    /// un-editable by the member themselves (S-13, FR-006).
    /// </para>
    ///
    /// <para>
    /// The linked account's copies are updated alongside, while both rows still carry them. Letting
    /// them diverge would mean the profile screen and the admin screen disagreeing about a phone
    /// number, with no way to tell which was right.
    /// </para>
    /// </summary>
    public static async Task<IResult> HandleAsync(
        Guid memberId,
        [FromBody] MemberRequest request,
        IMemberStore members,
        UserManager<ApplicationUser> userManager,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        if (!MemberRequestReader.TryRead(request, out var displayName, out var contact, out var failure))
        {
            return failure;
        }

        var member = await members.FindAsync(memberId, cancellationToken);
        if (member is null)
        {
            return Results.NotFound();
        }

        // MEMBER.EMAIL IS DELIBERATELY NOT ASSIGNED (S-17), and the omission is load-bearing rather
        // than an oversight. The admin form stopped sending an address, so writing the request's
        // value here would set every edit's email to null — wiping the login address the member
        // registered with, on an admin fixing a typo in a phone number. The address has exactly one
        // writer now, RegisterAsync, and nothing here may take it away.
        member.DisplayName = displayName;
        member.PhoneNumber = contact?.PhoneNumber;
        member.Street = contact?.Street;
        member.HouseNumber = contact?.HouseNumber;
        member.PostalCode = contact?.PostalCode;
        member.City = contact?.City;
        member.ConcurrencyStamp = Guid.NewGuid().ToString();

        if (member.UserId is not null)
        {
            var user = await userManager.FindByIdAsync(member.UserId);
            if (user is not null)
            {
                // The EMAIL IS NOT TOUCHED on the account. It is the login identifier, Identity keeps a
                // normalised copy and a uniqueness index over it, and changing it here would rename
                // somebody's username as a side effect of an admin fixing a typo in a phone number.
                // Changing a login address is its own operation and nobody has asked for it.
                user.DisplayName = displayName;
                // Only the phone, since S-14 Phase 8: the address is the member's and the account's
                // four columns go in the next release. PhoneNumber is Identity's own and survives,
                // so it is kept in step — the same rule ProfileEndpoints follows.
                user.PhoneNumber = contact?.PhoneNumber;
                user.ConcurrencyStamp = Guid.NewGuid().ToString();
            }
        }

        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            return Results.Json(new MemberFailure("conflict"), statusCode: 409);
        }

        return Results.NoContent();
    }
}
