using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Application.Scheduling;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// One member record in full.
/// </summary>
public static class GetMember
{
    public static async Task<IResult> HandleAsync(
        Guid memberId,
        IMemberQuery query,
        CancellationToken cancellationToken)
    {
        var member = await query.FindDetailAsync(memberId, cancellationToken);

        return member is null ? Results.NotFound() : Results.Ok(member);
    }
}
