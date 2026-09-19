using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Application.Scheduling;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// What the admin submits to create or edit a member record.
///
/// <para>
/// NO EMAIL ADDRESS, and its absence is the enforcement (S-17). The desk records who trains here;
/// the address arrives when that person registers with their invitation and becomes their login,
/// written once by <c>RegisterAsync</c> and never overwritten. Until then the club reaches them at
/// the counter and they receive no email and no push — an accepted consequence, recorded as IR-01,
/// not a gap to be filled in by typing an address here.
/// </para>
///
/// <para>
/// The five contact fields are ALL-OR-NOTHING rather than individually optional — see
/// <see cref="MemberAdminEndpoints"/>'s remarks on why they are not simply required here the way they
/// are at registration.
/// </para>
/// </summary>
public record MemberRequest(
    string DisplayName,
    string? PhoneNumber,
    string? Street,
    string? HouseNumber,
    string? PostalCode,
    string? City);
