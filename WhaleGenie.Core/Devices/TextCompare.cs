using System;
using System.Text.RegularExpressions;
using WhaleGenie.Core;

namespace WhaleGenie.Core.Devices;

/// <summary>
/// The two switches and the spacing rule that travel with one text comparison. An unanswered
/// <see cref="Trim"/> keeps the rule the five ways have always had: "exactly" trims, the rest
/// compare what they were given.
/// </summary>
public readonly record struct TextCompareOptions(
    bool CaseSensitive = false,
    bool? Trim = null,
    bool Multiline = false)
{
    public StringComparison Comparison => CaseSensitive
        ? StringComparison.Ordinal
        : StringComparison.OrdinalIgnoreCase;

    public RegexOptions Regex => (CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase)
        | (Multiline ? RegexOptions.Multiline : RegexOptions.None);

    public bool Trims(string way) => Trim ?? way == TextCompare.Exactly;
}

/// <summary>
/// How one piece of text is held up against another: contained in it, at one of its ends, exactly
/// it, or matching it as a regular expression. Everywhere the program compares text — a step looking
/// for writing on the screen, a condition about writing, a window recognised by its title, a switch
/// branch — it asks this one list, so "starts with" cannot come to mean two different things.
/// </summary>
/// <remarks>
/// The default still ignores case and trims both sides for "exactly it", because writing read off
/// a screen carries spacing a person typing it out would not. Each use may ask for case-sensitive
/// comparison, for an explicit trim rule, or for a regex that reads ^ and $ by line; the words
/// themselves stay the five a macro file carries.
/// </remarks>
public static class TextCompare
{
    /// <summary>The text holds what was looked for; the default, and what an unanswered field means.</summary>
    public const string Contains = "contains";

    /// <summary>The text starts with what was looked for.</summary>
    public const string StartsWith = "startsWith";

    /// <summary>The text ends with what was looked for.</summary>
    public const string EndsWith = "endsWith";

    /// <summary>
    /// The text is what was looked for, give or take the spacing at either end. It is called
    /// <c>Exactly</c> here rather than <c>Equals</c> only because that name is taken; the word a
    /// macro writes is the one in the value.
    /// </summary>
    public const string Exactly = "equals";

    /// <summary>What was looked for is a regular expression, and the text is matched against it.</summary>
    public const string Regex = "regex";

    private static readonly string[] Ways = [Contains, StartsWith, EndsWith, Exactly, Regex];

    /// <summary>The five, in the order the fields offer them.</summary>
    public static IReadOnlyList<string> Values => Ways;

    /// <summary>English fallback text for <see cref="Ways"/>, in the same order; the UI translates it.</summary>
    public static IReadOnlyList<string> Labels =>
        ["Contains", "Starts with", "Ends with", "Exactly", "Regex"];

    /// <summary>
    /// One of the five, as written. Empty is "nobody answered" and reads as
    /// <see cref="Contains"/>, which is also what a field left alone means everywhere else.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The text is none of the five. A wrong word is worth saying out loud rather than quietly
    /// comparing in some other way than the one that was written.
    /// </exception>
    public static string Read(string? written)
    {
        var text = (written ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return Contains;
        }

        foreach (var way in Ways)
        {
            if (string.Equals(way, text, StringComparison.OrdinalIgnoreCase))
            {
                return way;
            }
        }

        throw new ArgumentException(
            $"'{text}' is not one of the ways of comparing text: {string.Join(", ", Ways)}.", nameof(written));
    }

    /// <summary>Whether the text is held up as a pattern, which is the one way the machine can refuse.</summary>
    public static bool IsPattern(string? written) => Read(written) == Regex;

    /// <summary>Whether <paramref name="text"/> answers to <paramref name="wanted"/> the written way.</summary>
    public static bool Matches(string? text, string wanted, string? way,
        TextCompareOptions options = default)
    {
        var found = text ?? string.Empty;
        var read = Read(way);
        if (options.Trims(read))
        {
            found = found.Trim();
            wanted = wanted.Trim();
        }

        return read switch
        {
            StartsWith => found.StartsWith(wanted, options.Comparison),
            EndsWith => found.EndsWith(wanted, options.Comparison),
            Exactly => string.Equals(found, wanted, options.Comparison),
            Regex => RegexGuard.IsMatch(found, wanted, options.Regex),
            _ => found.Contains(wanted, options.Comparison),
        };
    }

    /// <summary>
    /// Reads a pattern ahead of time, so a pattern the machine cannot read is refused before anything
    /// is looked at rather than half-way through a screen. An empty pattern is not a pattern at all:
    /// it means "anything", the way an empty search text means "the first one".
    /// </summary>
    /// <exception cref="ArgumentException">The pattern cannot be read, thrown by <see cref="Regex"/> itself.</exception>
    public static void Check(string wanted, string? way, TextCompareOptions options = default)
    {
        if (IsPattern(way) && wanted.Length > 0)
        {
            _ = RegexGuard.Create(wanted, options.Regex);
        }
    }
}
