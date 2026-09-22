using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace po_prostu_silka.Application.Members;

/// <summary>
/// The single-use code that lets someone attach a new account to a member record the club already
/// keeps (S-14, AM-004 and AM-005).
///
/// <para>
/// IT IS READ ALOUD AND TYPED IN, and every decision here follows from that. The club hands it over at
/// the desk or down the phone, so the alphabet excludes every pair a person confuses when
/// transcribing — <c>0</c>/<c>O</c>, <c>1</c>/<c>I</c>/<c>L</c> — and the input is normalised hard
/// enough that a member who lowercases it, adds the display dash, or pads it with spaces still
/// succeeds. A code that is technically correct and practically unusable would just move the failure
/// from the system to the person at the counter.
/// </para>
///
/// <para>
/// Placed in Application beside <see cref="ContactDetails"/>, and for the same reason: the failure
/// codes it produces are the API's wire vocabulary, which is a contract of the HTTP surface rather
/// than a rule about a member. Pure BCL, so it unit-tests without a database.
/// </para>
/// </summary>
public static class MemberAccessCode
{
    /// <summary>
    /// Unambiguous uppercase alphabet: no <c>O</c>, <c>0</c>, <c>I</c>, <c>1</c>, <c>L</c>.
    ///
    /// 31 symbols over 8 characters is about 8.5 × 10¹¹ combinations, against a handful of live codes
    /// at any moment — which is what makes guessing uneconomic. It is not the only control: since
    /// S-16 <c>/register</c> carries a per-client-IP rate limit (RateLimitPolicies.Register), and with
    /// S-17 making the code the ONLY way in, that limiter is load-bearing rather than incidental. An
    /// earlier version of this comment argued the code space was large enough "even though /register
    /// carries no rate limit"; it has carried one since S-16, and the pair together is why no further
    /// hardening was added.
    /// </summary>
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    /// <summary>Characters in a code, excluding the display separator.</summary>
    public const int Length = 8;

    /// <summary>
    /// How long a freshly issued code stays valid.
    ///
    /// <para>
    /// A DEFAULT, NOT A RULE. The expiry is stored on the member row rather than derived from an
    /// issued-at timestamp precisely so that changing this number never re-dates a code already in
    /// somebody's hand — in either direction.
    /// </para>
    /// </summary>
    public static readonly TimeSpan Validity = TimeSpan.FromDays(14);

    /// <summary>
    /// Column width. Wider than <see cref="Length"/> so a future format could carry a separator
    /// without a migration; mirrored by MemberConfiguration.
    /// </summary>
    public const int MaxLength = 12;

    /// <summary>
    /// A new code, in the form it is STORED: uppercase, no separator.
    ///
    /// <para>
    /// <see cref="RandomNumberGenerator"/>, not <c>Random</c>. This is a credential — the only thing it
    /// grants is "attach the account I am creating to this member", but that is enough to inherit
    /// somebody's training history, and a predictable sequence would hand it over.
    /// </para>
    ///
    /// <para>
    /// <c>GetInt32</c> per character rather than masking bytes: 31 does not divide 256, so masking
    /// would make some letters likelier than others. The bias would be small and completely invisible,
    /// which is exactly the kind of thing not worth being clever about.
    /// </para>
    /// </summary>
    public static string Generate()
    {
        var characters = new char[Length];

        for (var i = 0; i < Length; i++)
        {
            characters[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(characters);
    }

    /// <summary>
    /// How the code is shown to the admin: <c>XXXX-XXXX</c>.
    ///
    /// The dash is presentation only and is stripped on the way back in — it exists because eight
    /// unbroken characters are hard to read aloud without losing your place.
    /// </summary>
    public static string Format(string stored) =>
        stored.Length == Length ? $"{stored[..4]}-{stored[4..]}" : stored;

    /// <summary>
    /// Normalises what somebody typed into the stored form.
    /// </summary>
    /// <returns>
    /// <c>true</c> with <paramref name="normalised"/> set when the input could be a code; <c>false</c>
    /// when it could not, which the endpoint answers as <c>invalid_member_code</c>.
    /// </returns>
    /// <remarks>
    /// STRICTLY A FORMAT CHECK. Whether a code exists, is expired, or was already used is a question
    /// about the database and is answered — as one indistinguishable refusal — by the endpoint.
    /// </remarks>
    public static bool TryNormalise(
        string? input,
        [NotNullWhen(true)] out string? normalised)
    {
        normalised = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        // Bound the work before scanning: a pathological input should not be walked character by
        // character. Generous enough to allow separators and stray spaces around a real code.
        if (input.Length > MaxLength * 4)
        {
            return false;
        }

        var characters = new char[Length];
        var count = 0;

        foreach (var raw in input)
        {
            // Everything a person adds between the characters — dashes, spaces, the odd dot — is
            // dropped rather than refused. Refusing the dash we ourselves printed would be perverse.
            if (raw is '-' or ' ' or '.' or '_')
            {
                continue;
            }

            var upper = char.ToUpperInvariant(raw);

            if (!Alphabet.Contains(upper) || count == Length)
            {
                return false;
            }

            characters[count++] = upper;
        }

        if (count != Length)
        {
            return false;
        }

        normalised = new string(characters);
        return true;
    }
}
