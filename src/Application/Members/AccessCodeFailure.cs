using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Application.Scheduling;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// Why issuing a code was refused. <c>has_account</c> — the member already has a login, so there is
/// nothing to claim; <c>conflict</c> — a lost optimistic race, or the generator lost every retry.
/// </summary>
public record AccessCodeFailure(string Reason);
