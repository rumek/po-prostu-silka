using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Application.Scheduling;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// Why a Trainer-role change was refused. <c>not_active</c> — the target's ACCOUNT is not Active.
///
/// <para>
/// WHAT THIS GUARD NOW GUARANTEES, AND WHAT IT NO LONGER DOES. It was written as a vetting rule:
/// FR-001 granted the role to an APPROVED account only, so refusing a non-Active account kept an
/// unvetted one out of the instructor selection S-06 builds on. S-16 retired approval, so Pending is
/// no longer produced and the only status this can still refuse is Blocked. The check stays correct
/// and stays worth having — the club must not hand a blocked person a class to run — but it is no
/// longer vetting anything, and nothing downstream may assume a trainer was ever reviewed by a human.
/// </para>
///
/// <c>no_account</c> — the member has no login, and roles live in Identity. S-14 makes an accountless
/// instructor REPRESENTABLE (the class's instructor is a member now) but deliberately still refuses
/// one; see roadmap Open Question 3.
///
/// <c>failed</c> — Identity refused the write. This IS a real concurrency failure, despite the role
/// change looking like a simple insert: AddToRoleAsync goes through UpdateUserAsync, which is a
/// read-then-write against the ConcurrencyStamp token, so a BlockAsync landing at the same moment
/// (it rotates that stamp) makes the role write lose. The account is then Blocked and holds no
/// Trainer role — NOT the outcome the caller asked for. What makes that safe is the SPA's generic
/// 409 branch, which refetches rather than patching the row from a guess.
/// </summary>
public record TrainerRoleFailure(string Reason);
