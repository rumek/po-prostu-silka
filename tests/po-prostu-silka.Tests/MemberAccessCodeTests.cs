using po_prostu_silka.Application.Members;

namespace po_prostu_silka.Tests;

/// <summary>
/// The member code's format rules (S-14). Pure BCL, no database, no fixture — which is the point:
/// these are the rules a person at the counter runs into, and they should be cheap to pin.
/// </summary>
public class MemberAccessCodeTests
{
    private const string Ambiguous = "OIL01";

    [Fact]
    public void A_generated_code_is_eight_characters_from_the_unambiguous_alphabet()
    {
        for (var i = 0; i < 200; i++)
        {
            var code = MemberAccessCode.Generate();

            Assert.Equal(MemberAccessCode.Length, code.Length);

            // THE WHOLE REASON THE ALPHABET IS WHAT IT IS: the code is read aloud and written down, so
            // no character may be confusable with another. A generator that let an O through would
            // produce codes that are correct and unusable.
            Assert.All(code, c => Assert.DoesNotContain(c, Ambiguous));
            Assert.All(code, c => Assert.True(char.IsUpper(c) || char.IsDigit(c)));
        }
    }

    [Fact]
    public void Two_generated_codes_are_not_the_same()
    {
        var codes = Enumerable.Range(0, 100).Select(_ => MemberAccessCode.Generate()).ToHashSet();

        // Not a proof of randomness — it is a smoke test that the generator is not returning a
        // constant, which is the failure mode a broken refactor actually produces.
        Assert.Equal(100, codes.Count);
    }

    [Fact]
    public void A_generated_code_round_trips_through_the_display_form()
    {
        var code = MemberAccessCode.Generate();

        Assert.True(MemberAccessCode.TryNormalise(MemberAccessCode.Format(code), out var normalised));
        Assert.Equal(code, normalised);
    }

    [Fact]
    public void The_display_form_is_two_groups_of_four()
    {
        Assert.Equal("ABCD-2345", MemberAccessCode.Format("ABCD2345"));
    }

    /// <summary>
    /// Everything a person plausibly types. Refusing the dash we printed ourselves would be perverse,
    /// and so would refusing lowercase from someone reading it off a note.
    /// </summary>
    [Theory]
    [InlineData("ABCD2345")]
    [InlineData("ABCD-2345")]
    [InlineData("abcd2345")]
    [InlineData("abcd-2345")]
    [InlineData("  ABCD-2345  ")]
    [InlineData("ABCD 2345")]
    [InlineData("A B C D 2 3 4 5")]
    public void Anything_a_member_might_type_normalises_to_the_stored_form(string input)
    {
        Assert.True(MemberAccessCode.TryNormalise(input, out var normalised));
        Assert.Equal("ABCD2345", normalised);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ABCD234")]
    [InlineData("ABCD23456")]
    [InlineData("ABCD-234!")]
    public void Malformed_input_is_refused(string? input)
    {
        Assert.False(MemberAccessCode.TryNormalise(input, out var normalised));
        Assert.Null(normalised);
    }

    /// <summary>
    /// The ambiguous characters are not silently mapped onto their look-alikes. Accepting "0" as "O"
    /// would be helpful right up until it matched somebody else's code.
    /// </summary>
    [Theory]
    [InlineData("ABCD234O")]
    [InlineData("ABCD2340")]
    [InlineData("ABCD234I")]
    [InlineData("ABCD234L")]
    public void Characters_outside_the_alphabet_are_refused_rather_than_corrected(string input)
    {
        Assert.False(MemberAccessCode.TryNormalise(input, out _));
    }

    /// <summary>
    /// A pathological input must be rejected on its length, not walked character by character —
    /// /register carries no rate limit, so the work per request is worth bounding.
    /// </summary>
    [Fact]
    public void An_absurdly_long_input_is_refused_without_scanning_it()
    {
        Assert.False(MemberAccessCode.TryNormalise(new string('A', 100_000), out _));
    }
}
