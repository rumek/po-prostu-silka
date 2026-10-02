using System.Security.Claims;
using po_prostu_silka.Application.Members;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>The read routes of makeups (S-36): the staff list, one item's eligible classes, the member's count.</summary>
public static class GetMakeups
{
    public const int PageSize = 25;

    /// <summary>
    /// The staff "Odrabianie" list - open and planned items, or every item with
    /// <paramref name="closed"/>. Admin and trainer see the whole club's list: any trainer may arrange
    /// any makeup.
    /// </summary>
    public static async Task<IResult> ListAsync(
        bool? closed,
        int? page,
        IMakeupQuery query,
        CancellationToken cancellationToken)
    {
        if (page is < 1)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["page"] = ["Must be 1 or greater."],
            });
        }

        return Results.Ok(await query.GetItemsAsync(closed == true, page ?? 1, PageSize, cancellationToken));
    }

    /// <summary>
    /// The classes the item's makeup may go into. Only an OPEN item has any: asking for a planned or
    /// closed one is refused <c>makeup_not_open</c> rather than answered with an empty list, so the
    /// picker never shows "no classes" for the wrong reason.
    /// </summary>
    public static async Task<IResult> ClassesAsync(
        Guid absenceBookingId,
        IMakeupQuery query,
        CancellationToken cancellationToken)
    {
        var item = await query.GetItemAsync(absenceBookingId, cancellationToken);
        if (item is null)
        {
            return Results.NotFound();
        }

        if (item.Status != "open")
        {
            return MakeupFailure.Refuse("makeup_not_open");
        }

        return Results.Ok(await query.GetEligibleClassesAsync(item.MemberId, item.Deadline, cancellationToken));
    }

    /// <summary>The signed-in member's open items: how many, and the nearest deadline.</summary>
    public static async Task<IResult> MineAsync(
        ClaimsPrincipal principal,
        IMakeupQuery query,
        CancellationToken cancellationToken)
    {
        var memberId = principal.GetMemberId();
        if (memberId is null)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(await query.GetForMemberAsync(memberId.Value, cancellationToken));
    }
}
