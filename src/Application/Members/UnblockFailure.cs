using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Application.Scheduling;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// Why an unblock was refused. <c>conflict</c> — someone changed the row between our read and our
/// write, so the caller's view is stale and should be refetched.
///
/// <c>not_blocked</c> is GONE as of S-14 and must not come back: membership has two states, so
/// "not blocked" is "already active", and that is a no-op the handler reports as success rather than
/// an error.
/// </summary>
public record UnblockFailure(string Reason);
