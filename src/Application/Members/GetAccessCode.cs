using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Application.Scheduling;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// Reads the live member code so the admin can hand it over (AM-004).
/// </summary>
public static class GetAccessCode
{
    /// <summary>
    /// The member's outstanding code, or 204 when there is none.
    ///
    /// <para>
    /// AN EXPIRED CODE IS REPORTED AS NONE. It is dead either way, and showing the admin a code that
    /// will be refused is worse than showing them nothing — they would read it out and the member
    /// would fail, which is the one outcome this screen exists to prevent.
    /// </para>
    /// </summary>
    public static async Task<IResult> HandleAsync(
        Guid memberId,
        IMemberStore members,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var member = await members.FindAsync(memberId, cancellationToken);
        if (member is null)
        {
            return Results.NotFound();
        }

        if (member.AccessCode is null
            || member.AccessCodeExpiresAt is null
            || member.AccessCodeExpiresAt <= timeProvider.GetUtcNow())
        {
            return Results.NoContent();
        }

        return Results.Ok(new AccessCodeView(
            MemberAccessCode.Format(member.AccessCode),
            member.AccessCodeExpiresAt.Value));
    }
}
