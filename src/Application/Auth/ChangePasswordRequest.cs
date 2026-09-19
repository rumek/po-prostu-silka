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
/// An in-session password change (S-13). The current password is required and is the whole
/// authorisation for the change - a live cookie alone is not enough, because an unattended session
/// is the exact scenario this guards against.
/// </summary>
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
