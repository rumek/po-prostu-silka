using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// Who the staff member is booking (S-14 AM-007, widened by S-16 MP-02).
///
/// <para>
/// A MEMBER ID IN A BODY. That used to be the exception among booking routes — every member-facing
/// one took its member from the cookie on principle — and since MP-01 removed those routes it is
/// simply how booking works: the person being booked is by definition not the person asking, and may
/// have no account to be a caller with at all. What makes it safe is the <c>TrainerOrAdmin</c> policy
/// on the group plus each handler's instructor check, never the shape of the request.
/// </para>
/// </summary>
public record AdminBookingRequest(Guid MemberId);
