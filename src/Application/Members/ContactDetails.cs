using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// The five contact fields a member supplies, validated and normalised in exactly one place (S-13).
///
/// <para>
/// WHY THIS TYPE EXISTS AT ALL. The same five fields arrive at two endpoints - <c>/api/auth/register</c>
/// and <c>PUT /api/profile</c> - and the slice's rule is that they are required at both. Written
/// twice, the two copies drift the first time a rule changes, and the drift shows up as a member who
/// can save a profile the registration form would have refused. One parse at the write boundary, two
/// callers, no second opinion.
/// </para>
///
/// <para>
/// NORMALISATION IS PART OF THE CONTRACT, not a nicety. The phone number is stored in one canonical
/// form (nine digits, no separators, no country code), so "+48 123 456 789" and "123456789" are the
/// same value in the database rather than two rows that look different to every future comparison.
/// </para>
///
/// <para>
/// Placed in Application, not Domain: the failure codes it returns are the API's wire vocabulary -
/// the literal strings the SPA maps onto form controls - and that is a contract of the HTTP surface,
/// not a rule about a member. Pure BCL all the same, so it unit-tests without a database.
/// </para>
/// </summary>
/// <param name="PhoneNumber">Exactly nine digits, no separators and no country code.</param>
/// <param name="Street">Trimmed street name.</param>
/// <param name="HouseNumber">Trimmed house/flat number.</param>
/// <param name="PostalCode">Trimmed postal code in NN-NNN form.</param>
/// <param name="City">Trimmed town or city.</param>
public record ContactDetails(
    string PhoneNumber,
    string Street,
    string HouseNumber,
    string PostalCode,
    string City)
{
    /// <summary>Column widths. Mirrored by ApplicationUserConfiguration - keep the two in step.</summary>
    public const int PhoneNumberMaxLength = 20;
    public const int StreetMaxLength = 100;
    public const int HouseNumberMaxLength = 20;
    public const int PostalCodeMaxLength = 6;
    public const int CityMaxLength = 100;

    /// <summary>
    /// Polish postal code, anchored on both ends. Without the anchors "123-4567" would match its own
    /// interior and be accepted.
    /// </summary>
    private static readonly Regex PostalCodePattern = new(
        @"^\d{2}-\d{3}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>A Polish subscriber number: nine digits, after the separators and +48 are stripped.</summary>
    private static readonly Regex NormalisedPhonePattern = new(
        @"^\d{9}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Validates and normalises the five fields. Returns <c>true</c> with <paramref name="details"/>
    /// holding the values to store, or <c>false</c> with <paramref name="reason"/> holding the single
    /// failure code the endpoint hands back to the SPA.
    /// </summary>
    /// <remarks>
    /// Fields are checked in the order they appear on the form, so a member fixing errors one at a
    /// time is walked down the page rather than around it. Only the first failure is reported -
    /// matching how the registration endpoint already answers.
    /// </remarks>
    public static bool TryCreate(
        string? phoneNumber,
        string? street,
        string? houseNumber,
        string? postalCode,
        string? city,
        [NotNullWhen(true)] out ContactDetails? details,
        [NotNullWhen(false)] out string? reason)
    {
        details = null;
        reason = null;

        if (!TryNormalisePhone(phoneNumber, out var normalisedPhone))
        {
            reason = "invalid_phone";
            return false;
        }

        if (!TryTrim(street, StreetMaxLength, out var trimmedStreet))
        {
            reason = "invalid_street";
            return false;
        }

        if (!TryTrim(houseNumber, HouseNumberMaxLength, out var trimmedHouseNumber))
        {
            reason = "invalid_house_number";
            return false;
        }

        if (!TryTrim(postalCode, PostalCodeMaxLength, out var trimmedPostalCode)
            || !PostalCodePattern.IsMatch(trimmedPostalCode))
        {
            reason = "invalid_postal_code";
            return false;
        }

        if (!TryTrim(city, CityMaxLength, out var trimmedCity))
        {
            reason = "invalid_city";
            return false;
        }

        details = new ContactDetails(
            normalisedPhone,
            trimmedStreet,
            trimmedHouseNumber,
            trimmedPostalCode,
            trimmedCity);

        return true;
    }

    /// <summary>
    /// Strips everything a member might type between the digits, drops a leading country code, and
    /// requires what is left to be nine digits.
    ///
    /// <para>
    /// Both <c>+48</c> and a bare <c>48</c> prefix are dropped, but only once the string is already
    /// eleven digits long - otherwise "481234567" (a real nine-digit number starting with 48) would
    /// lose its first two digits.
    /// </para>
    /// </summary>
    private static bool TryNormalisePhone(string? input, out string normalised)
    {
        normalised = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var trimmed = input.Trim();

        // Guard the length before stripping: a pathological input should not be scanned character
        // by character, and anything this long is not a phone number.
        if (trimmed.Length > PhoneNumberMaxLength * 2)
        {
            return false;
        }

        var digits = new string([.. trimmed
            .Where(c => !char.IsWhiteSpace(c) && c != '-' && c != '(' && c != ')')]);

        if (digits.StartsWith('+'))
        {
            digits = digits[1..];
        }

        if (digits.Length == 11 && digits.StartsWith("48", StringComparison.Ordinal))
        {
            digits = digits[2..];
        }

        if (!NormalisedPhonePattern.IsMatch(digits))
        {
            return false;
        }

        normalised = digits;
        return true;
    }

    private static bool TryTrim(string? input, int maxLength, out string trimmed)
    {
        trimmed = input?.Trim() ?? string.Empty;
        return trimmed.Length > 0 && trimmed.Length <= maxLength;
    }
}
