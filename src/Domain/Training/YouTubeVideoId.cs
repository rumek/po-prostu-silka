using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace po_prostu_silka.Domain.Training;

/// <summary>
/// Turns whatever the admin pasted into the canonical 11-character YouTube video id, or refuses it
/// (prd.md FR-019).
///
/// <para>
/// PARSING HAPPENS AT THE WRITE BOUNDARY, AND THAT IS THE POINT. An exercise stores the id, never
/// the pasted URL: the thumbnail on the list and the player on the detail screen are both composed
/// from it, so there is exactly one representation and it is known-good. Storing the raw string
/// instead would move this parse to render time, where a bad link shows up as a broken image days
/// later instead of as a refusal on the field the admin is looking at.
/// </para>
///
/// <para>
/// The accepted shapes are the ones YouTube's own share, embed and address bars produce. Anything
/// else - another host, a playlist without a video, an id of the wrong length - is refused rather
/// than guessed at, because a guess here is a broken library entry nobody notices.
/// </para>
///
/// <para>
/// Pure BCL, no dependencies: Domain references nothing, and this is unit-tested without a database
/// (<c>YouTubeVideoIdTests</c>).
/// </para>
/// </summary>
public static class YouTubeVideoId
{
    /// <summary>
    /// A video id is exactly 11 characters of URL-safe base64. Anchored on both ends: without the
    /// anchors, a 12-character id would match its own first 11 characters and be silently accepted.
    /// </summary>
    private static readonly Regex IdPattern = new(
        "^[A-Za-z0-9_-]{11}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    /// <summary>
    /// Hosts whose URLs carry a video id. Checked explicitly rather than by a "contains youtube"
    /// test, so <c>https://not-youtube.example/watch?v=...</c> is refused.
    /// </summary>
    private static readonly string[] WatchHosts =
    [
        "youtube.com",
        "www.youtube.com",
        "m.youtube.com",
        "music.youtube.com",
        "youtube-nocookie.com",
        "www.youtube-nocookie.com",
    ];

    private static readonly string[] ShortHosts = ["youtu.be", "www.youtu.be"];

    /// <summary>
    /// Path segments that are followed by the id itself, e.g. <c>/embed/&lt;id&gt;</c>. <c>/watch</c>
    /// is handled separately because there the id lives in the query string.
    /// </summary>
    private static readonly string[] IdBearingSegments = ["embed", "shorts", "live", "v"];

    /// <summary>
    /// Extracts the video id from a bare id or from any of the link shapes YouTube hands out:
    /// <c>watch?v=</c>, <c>youtu.be/</c>, <c>/embed/</c>, <c>/shorts/</c>, <c>/live/</c>, with or
    /// without a scheme, with or without a <c>www.</c> / <c>m.</c> prefix, and with any number of
    /// extra query parameters (a <c>?t=42</c> from "share at current time" is the common one).
    /// </summary>
    /// <returns><c>true</c> when <paramref name="videoId"/> holds a valid id; otherwise <c>false</c>
    /// with <paramref name="videoId"/> set to the empty string.</returns>
    public static bool TryParse([NotNullWhen(true)] string? input, out string videoId)
    {
        videoId = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var trimmed = input.Trim();

        // The admin may paste the id alone - accepted, and cheapest to check first.
        if (IdPattern.IsMatch(trimmed))
        {
            videoId = trimmed;
            return true;
        }

        // A pasted "youtube.com/watch?v=..." has no scheme and is not an absolute Uri without one.
        var candidateUrl = trimmed.Contains("://", StringComparison.Ordinal)
            ? trimmed
            : "https://" + trimmed;

        if (!Uri.TryCreate(candidateUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        var segments = uri
            .AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .ToArray();

        var candidate = host switch
        {
            _ when ShortHosts.Contains(host) => segments.FirstOrDefault(),
            _ when WatchHosts.Contains(host) => FromYouTubePath(uri, segments),
            _ => null,
        };

        if (candidate is null || !IdPattern.IsMatch(candidate))
        {
            return false;
        }

        videoId = candidate;
        return true;
    }

    /// <summary>Rebuilds the canonical watch URL, which is what the edit form shows.</summary>
    public static string ToWatchUrl(string videoId) => $"https://www.youtube.com/watch?v={videoId}";

    private static string? FromYouTubePath(Uri uri, string[] segments)
    {
        if (segments.Length == 0)
        {
            return null;
        }

        if (string.Equals(segments[0], "watch", StringComparison.OrdinalIgnoreCase))
        {
            return QueryValue(uri.Query, "v");
        }

        // /embed/<id>, /shorts/<id>, /live/<id>, /v/<id>
        if (
            segments.Length >= 2
            && IdBearingSegments.Contains(segments[0], StringComparer.OrdinalIgnoreCase)
        )
        {
            return segments[1];
        }

        return null;
    }

    /// <summary>
    /// Reads one query parameter by hand rather than through a web helper, so this type stays pure
    /// BCL and Domain keeps referencing nothing.
    /// </summary>
    private static string? QueryValue(string query, string key)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            if (
                string.Equals(pair[..separator], key, StringComparison.OrdinalIgnoreCase)
            )
            {
                return Uri.UnescapeDataString(pair[(separator + 1)..]);
            }
        }

        return null;
    }
}
