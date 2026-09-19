using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Application.Scheduling;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// Why a block was refused. <c>is_admin</c> — the target holds the Admin role and is not a member;
/// <c>conflict</c> — someone changed the row between our read and our write, so the caller's view is
/// stale and should be refetched.
/// </summary>
public record BlockFailure(string Reason);
