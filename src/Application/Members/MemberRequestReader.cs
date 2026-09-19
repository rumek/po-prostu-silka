using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Application.Scheduling;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// Validates and normalises a <see cref="MemberRequest"/> for the create and update paths.
///
/// <para>
/// SHARED BY TWO HANDLERS, which is why it is here rather than in either of them - the two
/// paths must accept exactly the same input or the edit screen and the create screen drift.
/// </para>
/// </summary>
internal static class MemberRequestReader
{
    /// <summary>
    /// Validates the fields shared by create and edit, in the order they appear on the form.
    ///
    /// <paramref name="contact"/> comes back null when the caller supplied none of the five fields,
    /// which is the "recorded at the desk with nothing but a name" case. Supplying SOME of them is a
    /// failure, not a partial save.
    ///
    /// <para>
    /// There is no email out-parameter any more (S-17): the admin does not supply an address, so
    /// there is nothing here to validate or normalise.
    /// </para>
    /// </summary>
    public static bool TryRead(
        MemberRequest request,
        out string displayName,
        out ContactDetails? contact,
        out IResult failure)
    {
        displayName = string.Empty;
        contact = null;
        failure = Results.Empty;

        var trimmedName = request.DisplayName?.Trim() ?? string.Empty;
        if (trimmedName.Length == 0 || trimmedName.Length > 100)
        {
            failure = Results.Json(new MemberFailure("invalid_display_name"), statusCode: 400);
            return false;
        }

        displayName = trimmedName;

        var supplied = new[]
        {
            request.PhoneNumber, request.Street, request.HouseNumber, request.PostalCode, request.City,
        };

        if (supplied.All(string.IsNullOrWhiteSpace))
        {
            return true;
        }

        if (!ContactDetails.TryCreate(
                request.PhoneNumber,
                request.Street,
                request.HouseNumber,
                request.PostalCode,
                request.City,
                out var details,
                out var contactFailure))
        {
            failure = Results.Json(new MemberFailure(contactFailure), statusCode: 400);
            return false;
        }

        contact = details;
        return true;
    }
}
