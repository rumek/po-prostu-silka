using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// Shared validation and projection for the karnet write paths.
///
/// <para>
/// ENTRIES LEFT IS DERIVED, never stored - it is counted from the active bookings carrying
/// this pass's id. That is why the projection lives beside the validator rather than in each
/// handler: three write paths answer with the same view, and a second derivation is a second
/// chance to count it differently.
/// </para>
/// </summary>
public static class MembershipPassProjection
{
    /// <summary>
    /// Validates the shape of a request — the parts that are true regardless of what is in the
    /// database. Whether the range collides with another pass is a question about rows and is answered
    /// by the caller.
    /// </summary>
    /// <returns>
    /// <c>true</c> with the trimmed name; <c>false</c> with a 400 the caller returns unchanged.
    /// 400 rather than 409 throughout: these are malformed inputs, not conflicts with existing state.
    /// </returns>
    public static bool TryRead(
        IssuePassRequest request,
        out string typeName,
        out IResult failure)
    {
        typeName = request.TypeName?.Trim() ?? string.Empty;

        if (typeName.Length == 0 || typeName.Length > MembershipPassRules.TypeNameMaxLength)
        {
            failure = Results.Json(new MembershipPassFailure("invalid_type_name"), statusCode: 400);
            return false;
        }

        // INCLUSIVE, so a one-day pass has ValidFrom == ValidTo and is legal. Only a range that runs
        // backwards is refused.
        if (request.ValidTo < request.ValidFrom)
        {
            failure = Results.Json(new MembershipPassFailure("invalid_range"), statusCode: 400);
            return false;
        }

        // DayNumber arithmetic, +1 because both ends count: the 1st to the 1st is one day, not zero.
        if (request.ValidTo.DayNumber - request.ValidFrom.DayNumber + 1 > MembershipPassRules.MaxValidityDays)
        {
            failure = Results.Json(new MembershipPassFailure("invalid_range"), statusCode: 400);
            return false;
        }

        if (request.EntryCount < MembershipPassRules.MinEntryCount
            || request.EntryCount > MembershipPassRules.MaxEntryCount)
        {
            failure = Results.Json(new MembershipPassFailure("invalid_entry_count"), statusCode: 400);
            return false;
        }

        failure = Results.Empty;
        return true;
    }

    /// <summary>
    /// How many entries this pass has already spent, read through the same projection the screen
    /// reads — so the number the handler refuses on and the number the admin is looking at cannot
    /// disagree.
    /// </summary>
    public static async Task<int> EntriesUsedAsync(
        IMembershipPassQuery query,
        Guid memberId,
        Guid passId,
        CancellationToken cancellationToken)
    {
        var history = await query.GetForMemberAsync(memberId, cancellationToken);

        return history.FirstOrDefault(x => x.Id == passId)?.EntriesUsed ?? 0;
    }

    /// <summary>
    /// The wire view of a pass the handler already holds, with a used count it already knows.
    /// </summary>
    public static Task<MembershipPassView> ViewOfAsync(
        MembershipPass pass,
        int entriesUsed,
        TimeProvider timeProvider)
    {
        // Club-local, not UTC and not the server's clock: "does this pass cover today" is a question
        // about the day at the gym. Same reasoning the booking gate uses for the class's date.
        var today = DateOnly.FromDateTime(
            Domain.Scheduling.ClubTime.ToClubLocal(timeProvider.GetUtcNow()).DateTime);

        return Task.FromResult(new MembershipPassView(
            pass.Id,
            pass.TypeName,
            pass.ValidFrom,
            pass.ValidTo,
            pass.EntryCount,
            entriesUsed,
            pass.EntryCount - entriesUsed,
            pass.IssuedAt,
            pass.ValidFrom <= today && today <= pass.ValidTo));
    }
}
