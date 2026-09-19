using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Auth;
using po_prostu_silka.Domain;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// Why a profile save failed. Same five reason codes registration answers with, from the same
/// <see cref="ContactDetails"/> helper - one vocabulary the SPA maps onto controls once.
/// </summary>
public record ProfileFailure(string Reason);
