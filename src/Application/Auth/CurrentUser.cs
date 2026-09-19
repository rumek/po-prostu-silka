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
/// The session payload every authenticated screen reads.
///
/// The contact fields ride along rather than sitting behind their own GET (S-13): the profile form
/// pre-fills from session state, so the SPA needs no second round trip on a 5-DTU tier - and the
/// screen that prompts an incomplete account to fill them in can tell they are empty without asking.
/// They are nullable here and only here: accounts created before S-13 have none, and that is exactly
/// what the prompt keys off.
///
/// DisplayName and Email are deliberately absent from every write surface. The gym owns the name on
/// the membership; no endpoint in this app lets anyone change either.
/// </summary>
public record CurrentUser(
    string Id,
    string Email,
    string DisplayName,
    string Status,
    string[] Roles,
    string? PhoneNumber,
    string? Street,
    string? HouseNumber,
    string? PostalCode,
    string? City,
    Guid? MemberId,
    string? MembershipStatus);
