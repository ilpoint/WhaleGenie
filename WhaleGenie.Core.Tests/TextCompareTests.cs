using System;
using WhaleGenie.Core.Devices;
using Xunit;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The one list of ways of comparing text that the whole program asks: how each way behaves, what an
/// unanswered field means, and what a word that is none of them does.
/// </summary>
public class TextCompareTests
{
    [Theory]
    [InlineData(TextCompare.Contains, "Save the file", "the", true)]
    [InlineData(TextCompare.Contains, "Save the file", "save", true)]
    [InlineData(TextCompare.Contains, "Save the file", "Files", false)]
    [InlineData(TextCompare.StartsWith, "Save the file", "save", true)]
    [InlineData(TextCompare.StartsWith, "Save the file", "the", false)]
    [InlineData(TextCompare.EndsWith, "Save the file", "FILE", true)]
    [InlineData(TextCompare.EndsWith, "Save the file", "save", false)]
    [InlineData(TextCompare.Exactly, "Save the file", "save the file", true)]
    [InlineData(TextCompare.Exactly, "Save the file", "save", false)]
    [InlineData(TextCompare.Regex, "Save the file", @"^save.*e$", true)]
    [InlineData(TextCompare.Regex, "Save the file", @"^\d+$", false)]
    public void Every_way_answers_what_it_says(string way, string text, string wanted, bool expected)
        => Assert.Equal(expected, TextCompare.Matches(text, wanted, way));

    /// <summary>
    /// Writing read off a screen carries the spacing around it, so "exactly" is read with both sides
    /// trimmed: a line the model padded is still the line that was being looked for.
    /// </summary>
    [Fact]
    public void Exactly_ignores_the_spacing_at_either_end()
    {
        Assert.True(TextCompare.Matches("  Save ", "Save", TextCompare.Exactly));
        Assert.True(TextCompare.Matches("Save", "  Save  ", TextCompare.Exactly));
    }

    /// <summary>
    /// A field nobody answered means "contained in it", which is what every one of these fields has
    /// meant since before the five were written down in one place.
    /// </summary>
    [Fact]
    public void An_unanswered_way_is_contained_in_it()
    {
        Assert.Equal(TextCompare.Contains, TextCompare.Read(null));
        Assert.Equal(TextCompare.Contains, TextCompare.Read("   "));
        Assert.True(TextCompare.Matches("Save the file", "the", string.Empty));
    }

    /// <summary>
    /// Both spellings of one way are the same way, so a macro file written with the other case reads
    /// as what it says rather than falling back to something else.
    /// </summary>
    [Theory]
    [InlineData("CONTAINS", TextCompare.Contains)]
    [InlineData(" startsWith ", TextCompare.StartsWith)]
    [InlineData("EndsWith", TextCompare.EndsWith)]
    [InlineData("EQUALs", TextCompare.Exactly)]
    [InlineData("Regex", TextCompare.Regex)]
    public void A_way_is_read_however_it_is_spelled(string written, string expected)
        => Assert.Equal(expected, TextCompare.Read(written));

    /// <summary>
    /// A word that is none of the five is refused rather than quietly compared some other way: a
    /// macro that said "exactly this" must not end up reading "this is part of it".
    /// </summary>
    [Fact]
    public void A_word_that_is_none_of_the_five_is_refused()
    {
        var refused = Assert.Throws<ArgumentException>(() => TextCompare.Read("exact"));
        Assert.Contains("contains", refused.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => TextCompare.Matches("abc", "b", "middle"));
    }

    /// <summary>
    /// Only a pattern can be refused by the machine, and it is refused while nothing has been looked
    /// at yet; everything else passes the check, an empty pattern included, because an empty pattern
    /// means "anything" rather than "nothing".
    /// </summary>
    [Fact]
    public void A_pattern_is_checked_before_anything_is_looked_at()
    {
        TextCompare.Check("notes", TextCompare.Regex);
        TextCompare.Check(string.Empty, TextCompare.Regex);
        TextCompare.Check("a (", TextCompare.Contains);

        // A pattern that cannot be read comes back as the exception the pattern reader throws, which
        // is an ArgumentException; a shape that is not a pattern at all is never read.
        Assert.ThrowsAny<ArgumentException>(() => TextCompare.Check("a (", TextCompare.Regex));
        Assert.True(TextCompare.IsPattern(TextCompare.Regex));
        Assert.False(TextCompare.IsPattern(TextCompare.Contains));
        Assert.False(TextCompare.IsPattern(null));
    }

    /// <summary>
    /// The five are offered in one order and are all there is: the fields built from this list cannot
    /// offer a way the engine does not know, or leave one out.
    /// </summary>
    [Fact]
    public void The_five_are_listed_with_a_label_each()
    {
        Assert.Equal(
            [TextCompare.Contains, TextCompare.StartsWith, TextCompare.EndsWith,
             TextCompare.Exactly, TextCompare.Regex],
            TextCompare.Values);
        Assert.Equal(TextCompare.Values.Count, TextCompare.Labels.Count);
    }
}
