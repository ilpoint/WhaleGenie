namespace WhaleGenie.Core.Devices;

/// <summary>
/// How a step picks a window out, written into the macro file as one value. A rule normally chooses
/// one text part and one of the shared comparisons; the final choice instead names the exact live
/// instance by its handle. The two questions the window device takes — which part of a window, and
/// how it is compared — are one question to whoever writes the step, so they are written as one.
/// Text is compared by the same five ways as everywhere else in the program
/// (<see cref="TextCompare"/>): a title may also be held up to its end, or told to be exactly the
/// text, and a process name or a window class — an identifier read whole — may be told to be
/// exactly it, which "notepad" contained in the name would not do.
/// </summary>
public static class WindowHow
{
    /// <summary>The title holds the text. The default, and what a step from before the choice meant.</summary>
    public const string TitleContains = "titleContains";

    /// <summary>The title starts with the text, which is how a document's name at the end is left out.</summary>
    public const string TitleStartsWith = "titleStartsWith";

    /// <summary>The title ends with the text, which is how an application's name at the end is left out.</summary>
    public const string TitleEndsWith = "titleEndsWith";

    /// <summary>The title is the text, give or take the spacing at either end.</summary>
    public const string TitleEquals = "titleEquals";

    /// <summary>The title matches the text as a regular expression, for a title that changes per run.</summary>
    public const string TitleRegex = "titleRegex";

    /// <summary>The name of the program that owns the window holds the text, without the ".exe".</summary>
    public const string Process = "process";

    /// <summary>The name of the program that owns the window is the text, without the ".exe".</summary>
    public const string ProcessEquals = "processEquals";

    /// <summary>The class the program registered holds the text, such as "Notepad".</summary>
    public const string Class = "class";

    /// <summary>The class the program registered is the text, such as "Notepad".</summary>
    public const string ClassEquals = "classEquals";

    /// <summary>The system handle of this exact window instance, for telling two identical windows apart.</summary>
    public const string Handle = "handle";

    private static readonly string[] Ways =
    [
        TitleContains, TitleStartsWith, TitleEndsWith, TitleEquals, TitleRegex,
        Process, ProcessEquals, Class, ClassEquals, Handle,
    ];

    /// <summary>Every way of the ten, in the order the field offers them.</summary>
    public static IReadOnlyList<string> Values => Ways;

    /// <summary>English fallback text for <see cref="Ways"/>, in the same order; the UI translates it.</summary>
    public static IReadOnlyList<string> Labels =>
    [
        "Title contains",
        "Title starts with",
        "Title ends with",
        "Title is exactly",
        "Title matches a pattern",
        "Process name contains",
        "Process name is exactly",
        "Window class contains",
        "Window class is exactly",
        "Window handle (this instance)",
    ];

    /// <summary>
    /// Where one of the ten sits in that order, which is how a list of them is kept in step with
    /// the values behind it. Anything the ten do not name sits at the front, where the way that
    /// reads as "the title holds the text" already is.
    /// </summary>
    public static int IndexOf(string? written)
    {
        var at = Array.IndexOf(Ways, (written ?? string.Empty).Trim());
        return at < 0 ? 0 : at;
    }

    /// <summary>
    /// Reads what a step wrote back into the two parts the window device works with. Anything the
    /// ten do not name reads as the title holding the text, which is what a macro written before
    /// this choice existed meant. It is a fallback rather than a refusal because this field is read
    /// wherever a window is recognised — the trigger that watches windows, the list of windows a step
    /// would pick out — and a word nobody recognises has always meant the oldest reading here.
    /// </summary>
    public static (WindowMatch Match, string Compare) Read(string? written)
        => written?.Trim() switch
        {
            TitleStartsWith => (WindowMatch.Title, TextCompare.StartsWith),
            TitleEndsWith => (WindowMatch.Title, TextCompare.EndsWith),
            TitleEquals => (WindowMatch.Title, TextCompare.Exactly),
            TitleRegex => (WindowMatch.Title, TextCompare.Regex),
            Process => (WindowMatch.Process, TextCompare.Contains),
            ProcessEquals => (WindowMatch.Process, TextCompare.Exactly),
            Class => (WindowMatch.ClassName, TextCompare.Contains),
            ClassEquals => (WindowMatch.ClassName, TextCompare.Exactly),
            Handle => (WindowMatch.Handle, TextCompare.Exactly),
            _ => (WindowMatch.Title, TextCompare.Contains),
        };

    /// <summary>
    /// Whether the text is held up against a window as a pattern, which is the one way of the five
    /// the machine can refuse to read.
    /// </summary>
    public static bool IsPattern(string? written) => TextCompare.IsPattern(Read(written).Compare);

    /// <summary>Whether the chosen part is compared as text and therefore reads the three switches.</summary>
    public static bool UsesTextComparison(string? written) => Read(written).Match is not WindowMatch.Handle;

    /// <summary>
    /// Whether a window answers to the text, the way the step says to compare them. One place
    /// rather than one per device: the window listing and the UI Automation search both recognise
    /// a window by the same rule, and they should not be able to disagree about what "starts with"
    /// means.
    /// </summary>
    public static bool Matches(string? text, string wanted, string compare,
        TextCompareOptions options = default)
        => TextCompare.Matches(text, wanted, compare, options);

    /// <summary>
    /// Refuses a pattern the machine cannot read by throwing what
    /// <see cref="Regex"/> itself throws, so whoever asked can turn it into a failed step. An empty
    /// text is not a pattern at all: it means "the window in front" rather than "match nothing".
    /// </summary>
    public static void Check(string wanted, string compare, TextCompareOptions options = default)
        => TextCompare.Check(wanted, compare, options);
}
