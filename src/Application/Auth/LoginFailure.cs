using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;

namespace po_prostu_silka.Application.Auth;

/// <summary>
/// Why the login failure is named: S-02's blocked members need a different message from a wrong
/// password. Callers must not treat <c>invalid_credentials</c> as "no such account" - it also covers
/// a wrong password.
///
/// <c>pending_approval</c> is unreachable and has been since S-01, which let a pending member sign in
/// rather than refusing them; S-16 then removed the pending state entirely. The literal survives in
/// the SPA's LoginFailureReason union, and removing it there is churn for no gain — nothing sends
/// it.
/// </summary>
public record LoginFailure(string Reason);
