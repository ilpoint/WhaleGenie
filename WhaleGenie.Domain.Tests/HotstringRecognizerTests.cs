using WhaleGenie.Models;
using WhaleGenie.Runtime;

namespace WhaleGenie.Domain.Tests;

public class HotstringRecognizerTests
{
    [Fact]
    public void An_ending_character_is_required_by_default_and_is_replayed_after_cleanup()
    {
        var recognizer = new HotstringRecognizer();
        var entry = Choice("sig");

        Assert.Null(recognizer.Press("s", "S", [entry]));
        Assert.Null(recognizer.Press("i", "I", [entry]));
        Assert.Null(recognizer.Press("g", "G", [entry]));
        var hit = recognizer.Press(" ", "Space", [entry]);

        Assert.NotNull(hit);
        Assert.Equal(3, hit!.Backspaces);
        Assert.True(hit.SuppressEndingKey);
        Assert.True(hit.ReplayEndingKey);
        Assert.Equal(" ", hit.EndingText);
    }

    [Fact]
    public void Case_and_word_boundaries_follow_the_entry()
    {
        var recognizer = new HotstringRecognizer();
        var exact = Choice("Sig", new HotstringSpec("Sig", CaseSensitive: true));

        foreach (var character in "sig")
        {
            recognizer.Press(character.ToString(), character.ToString(), [exact]);
        }

        Assert.Null(recognizer.Press(" ", "Space", [exact]));

        recognizer.Reset();
        var insensitive = Choice("Sig");
        foreach (var character in "xsig")
        {
            recognizer.Press(character.ToString(), character.ToString(), [insensitive]);
        }

        Assert.Null(recognizer.Press(" ", "Space", [insensitive]));

        recognizer.Reset();
        var insideWord = Choice("Sig", new HotstringSpec("Sig", InsideWord: true));
        foreach (var character in "xsig")
        {
            recognizer.Press(character.ToString(), character.ToString(), [insideWord]);
        }

        Assert.NotNull(recognizer.Press(" ", "Space", [insideWord]));
    }

    [Fact]
    public void An_entry_may_fire_without_an_ending_key_or_keep_the_typed_text()
    {
        var recognizer = new HotstringRecognizer();
        var entry = Choice("go", new HotstringSpec("go", RequireEndChar: false, EraseTyped: false));

        recognizer.Press("g", "G", [entry]);
        var hit = recognizer.Press("o", "O", [entry]);

        Assert.NotNull(hit);
        Assert.False(hit!.SuppressEndingKey);
        Assert.Equal(0, hit.Backspaces);
        Assert.False(hit.ReplayEndingKey);
    }

    [Fact]
    public void The_longest_matching_abbreviation_wins()
    {
        var shortEntry = Choice("log");
        var longEntry = Choice(";log");
        var recognizer = new HotstringRecognizer();

        foreach (var character in ";log")
        {
            recognizer.Press(character.ToString(), character.ToString(), [shortEntry, longEntry]);
        }

        var hit = recognizer.Press(" ", "Space", [shortEntry, longEntry]);

        Assert.Equal(";log", hit!.Id);
        Assert.Equal(4, hit.Backspaces);
    }

    [Fact]
    public void Resetting_forgets_the_half_typed_string()
    {
        var recognizer = new HotstringRecognizer();
        var entry = Choice("go");

        recognizer.Press("g", "G", [entry]);
        recognizer.Reset();
        recognizer.Press("o", "O", [entry]);

        Assert.Null(recognizer.Press(" ", "Space", [entry]));
    }

    [Fact]
    public void An_entry_can_replace_the_default_ending_character_set()
    {
        var recognizer = new HotstringRecognizer();
        var entry = Choice("go", new HotstringSpec("go", EndChars: "\n"));

        recognizer.Press("g", "G", [entry]);
        recognizer.Press("o", "O", [entry]);
        Assert.Null(recognizer.Press(" ", "Space", [entry]));

        recognizer.Reset();
        recognizer.Press("g", "G", [entry]);
        recognizer.Press("o", "O", [entry]);
        Assert.NotNull(recognizer.Press("\n", "Enter", [entry]));
    }

    [Fact]
    public void Literal_ending_characters_are_explained_together()
    {
        Assert.Contains("Space", HotstringCharacters.DescribeEndChars(null));
        Assert.Contains("Enter", HotstringCharacters.DescribeEndChars(null));
        Assert.Contains("Tab", HotstringCharacters.DescribeEndChars(null));
        Assert.Equal("Space", HotstringCharacters.DescribeEndChars(" "));
    }

    [Fact]
    public void Ending_characters_round_trip_as_csharp_characters_without_ahk_escapes()
    {
        var values = new[] { " ", "\n", "\t", "`", "n", "s" };
        var written = HotstringCharacters.WriteEndChars(values);

        Assert.Contains("`", written);
        Assert.Contains("n", written);
        Assert.Contains("s", written);
        Assert.Equal(values.ToHashSet(StringComparer.Ordinal),
            HotstringCharacters.ParseEndChars(written));
        Assert.DoesNotContain("\n", HotstringCharacters.ParseEndChars("`n"));
        Assert.Contains("`", HotstringCharacters.ParseEndChars("`n"));
        Assert.Contains("n", HotstringCharacters.ParseEndChars("`n"));
    }

    private static HotstringChoice Choice(string abbreviation, HotstringSpec? spec = null)
        => new(abbreviation, spec ?? new HotstringSpec(abbreviation));
}
