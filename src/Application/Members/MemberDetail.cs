using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Application.Scheduling;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// One member with everything the admin's edit form needs. Separate from <see cref="MemberSummary"/>
/// rather than widening it: the list renders dozens of rows and has no business shipping everybody's
/// home address to build a table.
/// </summary>
public record MemberDetail(
    Guid Id,
    string? UserId,
    string DisplayName,
    string? Email,
    string MembershipStatus,
    string? AccountStatus,
    IReadOnlyList<string> Roles,
    string? PhoneNumber,
    string? Street,
    string? HouseNumber,
    string? PostalCode,
    string? City,
    DateTimeOffset CreatedAt);
