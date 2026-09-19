using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Application.Scheduling;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// Why a create or edit was refused. <c>invalid_display_name</c> — blank or too long;
/// <c>conflict</c> — a lost optimistic race. The five contact codes (<c>invalid_phone</c> and
/// friends) come straight from <see cref="ContactDetails"/> and are the same strings
/// <c>PUT /api/profile</c> answers with.
///
/// <para>
/// <c>invalid_email</c> and <c>email_taken</c> are GONE (S-17). This endpoint no longer accepts an
/// address, so neither is reachable: there is nothing to malform and nothing to collide with. The
/// uniqueness check they guarded still runs, at the one place an address now enters a member record —
/// <c>POST /api/auth/register</c>, which keeps answering <c>email_taken</c>.
/// </para>
/// </summary>
public record MemberFailure(string Reason);
