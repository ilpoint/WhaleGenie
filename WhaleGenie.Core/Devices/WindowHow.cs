using System.Text.RegularExpressions;

namespace WhaleGenie.Core.Devices;

/// <summary>
/// How a step holds its text up against a window, written into the macro file as one value. A
/// window is recognised by the text and by one choice out of five, because "the title, contained
/// in it" is what a macro nearly always means and "the process name" is what it means when the
/// title moves with the document or with the language. The two questions the window device takes —
/// which part of a window, and how it is compared — are one question to whoever writes the step,
/// so they are written as one.
/// </summary>
public static class WindowHow
{
    /// <summary>The title holds the text. The default, and what a step from before the choice meant.</summary>
    public const string TitleContains = "titleContains";

    /// <summary>The title starts with the text, which is how a document's name at the end is left out.</summary>
    public const string TitleStartsWith = "titleStartsWith";

    /// <summary>The title matches the text as a regular expression, for a title that changes per run.</summary>
    public const string TitleRegex = "titleRegex";

    /// <summary>The name of the program that owns the window holds the text, without the ".exe".</summary>
    public const string Process = "process";

    /// <summary>The class the program registered holds the text, such as "Notepad".</summary>
    public const string Class = "class";

    /// <summary>The five, in the order the field offers them.</summary>
    public static IReadOnlyList<string> Values =>
        [TitleContains, TitleStartsWith, TitleRegex, Process, Class];

    /// <summary>
    /// Reads what a step wrote back into the two parts the window device works with. Anything the
    /// five do not name reads as the title holding the text, which is what a macro written before
    /// this choice existed meant.
    /// </summary>
    public static (WindowMatch Match, WindowCompare Compare) Read(string? written)
        => written?.Trim() switch
        {
            TitleStartsWith => (WindowMatch.Title, WindowCompare.StartsWith),
            TitleRegex => (WindowMatch.Title, WindowCompare.Regex),
            Process => (WindowMatch.Process, WindowCompare.Contains),
            Class => (WindowMatch.ClassName, WindowCompare.Contains),
            _ => (WindowMatch.Title, WindowCompare.Contains),
        };

    /// <summary>
    /// Whether the text is held up against a window as a pattern, which is the one way of the five
    /// the machine can refuse to read.
    /// </summary>
    public static bool IsPattern(string? written) => Read(written).Compare is WindowCompare.Regex;

    /// <summary>
    /// Whether a window answers to the text, the way the step says to compare them. One place
    /// rather than one per device: the window listing and the UI Automation search both recognise
    /// a window by the same rule, and they should not be able to disagree about what "starts with"
    /// means.
    /// </summary>
    public static bool Matches(string? text, string wanted, WindowCompare compare) => compare switch
    {
        WindowCompare.StartsWith => (text ?? string.Empty).StartsWith(wanted,
            StringComparison.OrdinalIgnoreCase),
        WindowCompare.Regex => Regex.IsMatch(text ?? string.Empty, wanted, RegexOptions.IgnoreCase),
        _ => (text ?? string.Empty).Contains(wanted, StringComparison.OrdinalIgnoreCase),
    };

    /// <summary>
    /// Refuses a pattern the machine cannot read by throwing what
    /// <see cref="Regex"/> itself throws, so whoever asked can turn it into a failed step. An empty
    /// text is not a pattern at all: it means "the window in front" rather than "match nothing".
    /// </summary>
    public static void Check(string wanted, WindowCompare compare)
    {
        if (compare is WindowCompare.Regex && wanted.Length > 0)
        {
            _ = new Regex(wanted, RegexOptions.IgnoreCase);
        }
    }
}
