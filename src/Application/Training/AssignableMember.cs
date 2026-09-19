using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Training;

namespace po_prostu_silka.Application.Training;

/// <summary>
/// A member a plan may be assigned to. Deliberately two fields: the trainer's picker needs a label
/// and an id, and nothing else on this surface should expose emails or account status to an account
/// that is not an admin.
/// </summary>
/// <param name="Id">The MEMBER's id (S-14), not an account's — which is what lets a person the club
/// recorded but who never registered be offered here at all.</param>
/// <param name="HasAccount">
/// Whether they can sign in. The picker says so, because a plan assigned to someone with no login is
/// real work the member will never see in the app until they claim their record.
/// </param>
public record AssignableMember(Guid Id, string DisplayName, bool HasAccount);
