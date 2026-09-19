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
/// Why the reset failed.
///
/// <c>invalid_token</c> deliberately covers an unknown address, a malformed token, a token belonging
/// to someone else, an already-used token AND an expired one. Splitting those apart would hand an
/// anonymous caller the account-enumeration oracle that <c>/forgot-password</c> is built to deny -
/// "expired" means the address exists. One code, and the screen says "poproś o nowy link".
/// </summary>
public record ResetPasswordFailure(string Reason);
