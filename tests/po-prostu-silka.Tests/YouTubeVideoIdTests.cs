using po_prostu_silka.Domain.Training;

namespace po_prostu_silka.Tests;

/// <summary>
/// The contract everything downstream trusts (S-10 phase 1; prd.md FR-019): whatever the admin
/// pasted becomes one canonical 11-character id, or is refused.
///
/// <para>
/// THE EXPECTED ID IS A LITERAL HERE, deliberately. Asserting against another call to
/// <see cref="YouTubeVideoId.TryParse"/> would compute the expectation with the function under test,
/// so a parser that consistently returns the wrong 11 characters would pass every case.
/// </para>
///
/// <para>
/// The rejection cases carry more weight than the acceptance ones. A shape this parser fails to
/// recognise is a refusal the admin sees immediately and can work around; a shape it accepts
/// wrongly is a broken thumbnail discovered days later by someone else.
/// </para>
///
/// <para>
/// No fixture and no collection: pure functions, so neither a database nor a container.
/// </para>
/// </summary>
public class YouTubeVideoIdTests
{
    /// <summary>The id used throughout - it contains both '-' and '_', the two characters a naive
    /// alphanumeric pattern would reject.</summary>
    private const string Id = "dQw4w9-gX_Q";

    public static TheoryData<string> AcceptedShapes() =>
        [
            // The id alone: what someone pastes after reading it out of another URL.
            Id,
            // The address bar.
            $"https://www.youtube.com/watch?v={Id}",
            $"http://www.youtube.com/watch?v={Id}",
            $"https://youtube.com/watch?v={Id}",
            $"https://m.youtube.com/watch?v={Id}",
            $"https://music.youtube.com/watch?v={Id}",
            // No scheme - what a paste from a mobile browser's address bar often looks like.
            $"www.youtube.com/watch?v={Id}",
            $"youtube.com/watch?v={Id}",
            // "Share" and "share at current time".
            $"https://youtu.be/{Id}",
            $"https://youtu.be/{Id}?t=42",
            $"youtu.be/{Id}",
            // The id is not the first query parameter - a video opened from inside a playlist.
            $"https://www.youtube.com/watch?list=PLabc123&v={Id}",
            $"https://www.youtube.com/watch?v={Id}&list=PLabc123&index=4",
            // Embed, shorts, live, and the legacy /v/ form.
            $"https://www.youtube.com/embed/{Id}",
            $"https://www.youtube-nocookie.com/embed/{Id}",
            $"https://www.youtube.com/shorts/{Id}",
            $"https://www.youtube.com/live/{Id}",
            $"https://www.youtube.com/v/{Id}",
            // Surrounding whitespace survives a copy-paste more often than not.
            $"  https://youtu.be/{Id}  ",
        ];

    /// <summary>
    /// Every shape YouTube hands out resolves to the SAME id. This is the property the storage
    /// decision rests on: the library holds one representation no matter where the link came from.
    /// </summary>
    [Theory]
    [MemberData(nameof(AcceptedShapes))]
    public void Every_accepted_shape_yields_the_same_id(string input)
    {
        Assert.True(YouTubeVideoId.TryParse(input, out var videoId));
        Assert.Equal(Id, videoId);
    }

    public static TheoryData<string?> RejectedShapes() =>
        [
            null,
            "",
            "   ",
            // Another host entirely. A "does it contain youtube" check would let the third one
            // through, which is exactly the mistake this case exists to catch.
            "https://vimeo.com/123456789",
            $"https://vimeo.com/watch?v={Id}",
            $"https://not-youtube.example/watch?v={Id}",
            // Right host, wrong id length - 10 and 12 characters.
            "https://www.youtube.com/watch?v=dQw4w9-gX_",
            "https://www.youtube.com/watch?v=dQw4w9-gX_QQ",
            "dQw4w9-gX_",
            "dQw4w9-gX_QQ",
            // Right host, no video: a playlist, a channel, the home page.
            "https://www.youtube.com/playlist?list=PLabc123",
            "https://www.youtube.com/@someChannel",
            "https://www.youtube.com/",
            "https://youtu.be/",
            // A watch URL with the parameter present but empty.
            "https://www.youtube.com/watch?v=",
            // Characters outside the id alphabet.
            "https://www.youtube.com/watch?v=dQw4w9!gX_Q",
            // Not a URL at all.
            "wyciskanie sztangi leżąc",
            // A scheme that is not http(s) - refused rather than trusted.
            $"javascript:alert('{Id}')",
        ];

    /// <summary>
    /// A refused link leaves the caller with an empty id, never a partial one - the endpoint turns
    /// this into 400 invalid_video_url on the field the admin is looking at.
    /// </summary>
    [Theory]
    [MemberData(nameof(RejectedShapes))]
    public void Unrecognised_input_is_refused(string? input)
    {
        Assert.False(YouTubeVideoId.TryParse(input, out var videoId));
        Assert.Equal(string.Empty, videoId);
    }

    /// <summary>
    /// The canonical URL is what the edit form shows, so it must round-trip back through the parser -
    /// otherwise editing an exercise twice without touching the video field would lose it.
    /// </summary>
    [Fact]
    public void The_canonical_watch_url_round_trips()
    {
        var url = YouTubeVideoId.ToWatchUrl(Id);

        Assert.Equal($"https://www.youtube.com/watch?v={Id}", url);
        Assert.True(YouTubeVideoId.TryParse(url, out var videoId));
        Assert.Equal(Id, videoId);
    }
}
