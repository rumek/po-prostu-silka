using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Notifications;

namespace po_prostu_silka.Application.Notifications;

public record VapidKeyResponse(string PublicKey);
