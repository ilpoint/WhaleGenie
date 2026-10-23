using WhaleGenie.Execution;
using WhaleGenie.Models;

namespace WhaleGenie.Tests;

public class HotstringRecognizerTests
{
    [Fact]
    public void An_ending_character_is_required_by_default_and_is_replayed_after_cleanup()
    {
        var recognizer = new HotstringRecognizer();
        var entry = Entry("sig");

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
        var entry = Entry("Sig");
        entry.CaseSensitive = true;

        foreach (var character in "sig")
        {
            recognizer.Press(character.ToString(), character.ToString(), [entry]);
        }

        Assert.Null(recognizer.Press(" ", "Space", [entry]));

        recognizer.Reset();
        entry.CaseSensitive = false;
        entry.MatchInsideWord = false;
        foreach (var character in "xsig")
        {
            recognizer.Press(character.ToString(), character.ToString(), [entry]);
        }

        Assert.Null(recognizer.Press(" ", "Space", [entry]));

        recognizer.Reset();
        entry.MatchInsideWord = true;
        foreach (var character in "xsig")
        {
            recognizer.Press(character.ToString(), character.ToString(), [entry]);
        }

        Assert.NotNull(recognizer.Press(" ", "Space", [entry]));
    }

    [Fact]
    public void An_entry_may_fire_without_an_ending_key_or_keep_the_typed_text()
    {
        var recognizer = new HotstringRecognizer();
        var entry = Entry("go");
        entry.RequireEndChar = false;
        entry.EraseTyped = false;

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
        var shortEntry = Entry("log");
        var longEntry = Entry(";log");
        var recognizer = new HotstringRecognizer();

        foreach (var character in ";log")
        {
            recognizer.Press(character.ToString(), character.ToString(), [shortEntry, longEntry]);
        }

        var hit = recognizer.Press(" ", "Space", [shortEntry, longEntry]);

        Assert.Same(longEntry, hit!.Entry);
        Assert.Equal(4, hit.Backspaces);
    }

    [Fact]
    public void Resetting_forgets_the_half_typed_string()
    {
        var recognizer = new HotstringRecognizer();
        var entry = Entry("go");

        recognizer.Press("g", "G", [entry]);
        recognizer.Reset();
        recognizer.Press("o", "O", [entry]);

        Assert.Null(recognizer.Press(" ", "Space", [entry]));
    }

    [Fact]
    public void An_entry_can_replace_the_default_ending_character_set()
    {
        var recognizer = new HotstringRecognizer();
        var entry = Entry("go");
        entry.HotstringEndChars = "\n";

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

    private static TriggerEntry Entry(string abbreviation) => new()
    {
        Kind = TriggerKind.Hotstring,
        MacroName = "macro",
        Abbreviation = abbreviation,
    };
}
