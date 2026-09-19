using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Auth;
using po_prostu_silka.Domain;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// What a member may change about themselves (S-13). The five contact fields, and nothing else.
///
/// <para>
/// DISPLAY NAME AND EMAIL ARE ABSENT ON PURPOSE, and their absence from this record is the whole
/// enforcement. The gym owns the name on the membership, so a member sending one is not refused with
/// an error - the field simply is not part of the contract and never reaches the entity. Do not add
/// them here "for completeness": that would silently make FR-006's rewritten rule false again.
/// </para>
/// </summary>
public record ProfileRequest(
    string PhoneNumber,
    string Street,
    string HouseNumber,
    string PostalCode,
    string City);
