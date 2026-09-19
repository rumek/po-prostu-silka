using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// Why a karnet action was refused. The full vocabulary, which the SPA maps to Polish messages as a
/// complete <c>Record</c>:
///
/// <list type="bullet">
/// <item><c>member_not_found</c> — no such member. Answered as 404, not in this record.</item>
/// <item><c>member_blocked</c> — the member's membership is not Active, so a pass would entitle them
/// to nothing. Refused for the same reason issuing an access code to a blocked member is.</item>
/// <item><c>invalid_type_name</c> — blank, or longer than
/// <see cref="MembershipPassRules.TypeNameMaxLength"/>.</item>
/// <item><c>invalid_range</c> — <c>ValidTo</c> before <c>ValidFrom</c>, or a span wider than
/// <see cref="MembershipPassRules.MaxValidityDays"/>.</item>
/// <item><c>invalid_entry_count</c> — outside
/// <see cref="MembershipPassRules.MinEntryCount"/>..<see cref="MembershipPassRules.MaxEntryCount"/>.</item>
/// <item><c>overlapping_pass</c> — another pass of this member already covers part of the range.
/// Passes are a HISTORY, not a stack.</item>
/// <item><c>has_active_bookings</c> — the pass paid for bookings that still hold spots, so it cannot
/// vanish underneath them.</item>
/// <item><c>conflict</c> — a lost optimistic race; the caller's view is stale and should be
/// refetched.</item>
/// </list>
/// </summary>
public record MembershipPassFailure(string Reason);
