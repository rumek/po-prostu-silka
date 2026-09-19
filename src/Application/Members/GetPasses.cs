using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// Every karnet a member holds, newest first (MP-01).
/// </summary>
public static class GetPasses
{
    /// <summary>
    /// The member's pass history, newest first — expired passes included.
    ///
    /// <para>
    /// HISTORY, NOT "THE CURRENT PASS". The admin's question at the desk is usually "what has this
    /// person bought and what did they use", which a single current row cannot answer. The row that
    /// covers today is marked with <see cref="MembershipPassView.CoversToday"/> rather than being
    /// returned separately, so the screen can highlight it without a second request.
    /// </para>
    ///
    /// No pagination: a member accumulates a handful of passes a year.
    /// </summary>
    public static async Task<IResult> HandleAsync(
        Guid memberId,
        IMemberStore members,
        IMembershipPassQuery passes,
        CancellationToken cancellationToken)
    {
        // Resolved first so an unknown member is a 404 rather than an empty list — "this person has no
        // passes" and "this person does not exist" are different answers and the screen acts on them
        // differently.
        if (await members.FindAsync(memberId, cancellationToken) is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(await passes.GetForMemberAsync(memberId, cancellationToken));
    }
}
