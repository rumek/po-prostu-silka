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
/// Sets a new password from an emailed token. The email travels with the token because Identity's
/// tokens are validated against a specific user - the token alone does not identify one.
/// </summary>
public record ResetPasswordRequest(string Email, string Token, string NewPassword);
