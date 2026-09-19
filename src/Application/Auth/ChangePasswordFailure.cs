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
/// Why the change failed. Two codes, both 400: <c>invalid_current_password</c> and
/// <c>invalid_new_password</c>. Identity's raw error text is never forwarded - same reasoning as
/// <see cref="RegisterFailure"/>.
///
/// Unlike /login, disclosure is not a concern here: the caller has already proved they own the
/// session, so telling them which of the two passwords was the problem leaks nothing and is the
/// difference between a fixable form and a dead end.
/// </summary>
public record ChangePasswordFailure(string Reason);
