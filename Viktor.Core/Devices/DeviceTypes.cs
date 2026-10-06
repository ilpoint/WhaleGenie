using System;
using System.Globalization;

namespace Viktor.Core.Devices;

/// <summary>A point on the desktop, in pixels.</summary>
public readonly record struct ScreenPoint(int X, int Y);

/// <summary>A width and height, in pixels.</summary>
public readonly record struct ScreenSize(int Width, int Height);

/// <summary>A colour read from, or compared against, the screen.</summary>
public readonly record struct PixelColor(byte R, byte G, byte B)
{
    /// <summary>
    /// Reads <c>#RRGGBB</c>, <c>RRGGBB</c> or <c>#RGB</c>. Anything else becomes
    /// <paramref name="fallback"/>.
    /// </summary>
    public static PixelColor Parse(string? text, PixelColor fallback = default)
    {
        var value = (text ?? string.Empty).Trim().TrimStart('#');
        if (value.Length == 3)
        {
            value = string.Concat(value[0], value[0], value[1], value[1], value[2], value[2]);
        }

        return value.Length == 6
               && int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var packed)
            ? new PixelColor((byte)(packed >> 16), (byte)(packed >> 8), (byte)packed)
            : fallback;
    }

    /// <summary>The colour as <c>#RRGGBB</c>.</summary>
    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    /// <summary>How far apart two colours are: 0 when they are the same, 1 at the extremes.</summary>
    public double DistanceTo(PixelColor other)
    {
        double dr = R - other.R;
        double dg = G - other.G;
        double db = B - other.B;
        return Math.Sqrt((dr * dr + dg * dg + db * db) / (3.0 * 255 * 255));
    }

    /// <summary>True when this colour is within <paramref name="tolerancePercent"/> of another.</summary>
    public bool Matches(PixelColor other, double tolerancePercent)
        => DistanceTo(other) * 100 <= Math.Max(0, tolerancePercent);

    public override string ToString() => ToHex();
}

/// <summary>A block of pixels, top row first, four bytes per pixel: blue, green, red, alpha.</summary>
public sealed record ImageFrame(int Width, int Height, byte[] Bgra)
{
    /// <summary>A frame with nothing in it.</summary>
    public static ImageFrame Empty { get; } = new(0, 0, []);

    public bool IsEmpty => Width <= 0 || Height <= 0 || Bgra.Length < (long)Width * Height * 4;

    /// <summary>The colour of one pixel. Points outside the frame read as black.</summary>
    public PixelColor this[int x, int y]
    {
        get
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height || IsEmpty)
            {
                return default;
            }

            var offset = ((long)y * Width + x) * 4;
            return new PixelColor(Bgra[offset + 2], Bgra[offset + 1], Bgra[offset]);
        }
    }
}

/// <summary>Where a reference image was found, and how well it matched.</summary>
public sealed record ImageMatch(double Score, ScreenPoint Location, ScreenSize Size)
{
    /// <summary>The middle of the match, which is what a click aims at.</summary>
    public ScreenPoint Center => new(Location.X + (Size.Width / 2), Location.Y + (Size.Height / 2));
}

/// <summary>One piece of text found on screen.</summary>
public sealed record TextSpan(string Text, ScreenPoint Location, ScreenSize Size, double Confidence)
{
    public ScreenPoint Center => new(Location.X + (Size.Width / 2), Location.Y + (Size.Height / 2));
}

/// <summary>
/// What to look for through UI Automation. Every part is optional. A window or a page often shows
/// the same control many times over — five rows of one list, four identical buttons — so
/// <paramref name="Index"/> says which of the matches a step means, counted the way a person counts
/// them on screen: across the row first, then down. One is the first one, which is what a step
/// written before this setting existed still asks for.
/// </summary>
public sealed record UiQuery(
    string? Name = null,
    string? AutomationId = null,
    string? ControlType = null,
    string? ClassName = null,
    string? WindowTitle = null,
    int Index = 1)
{
    /// <summary>True when the query would match anything, which is never what was meant.</summary>
    public bool IsEmpty
        => string.IsNullOrWhiteSpace(Name)
           && string.IsNullOrWhiteSpace(AutomationId)
           && string.IsNullOrWhiteSpace(ControlType)
           && string.IsNullOrWhiteSpace(ClassName)
           && string.IsNullOrWhiteSpace(WindowTitle);

    /// <summary>
    /// Reads selector text such as <c>Button[name='Save']</c> or
    /// <c>Edit[automationId='input', class='Edit']</c> back into the query it describes. The text
    /// before the bracket is the kind of control, and everything inside is a property written as
    /// <c>name=value</c>. Anything unrecognised is left out rather than refused, so a selector
    /// with a stray word in it still finds what the rest of it describes.
    /// </summary>
    public static UiQuery Parse(string? selector, string? windowTitle = null, int index = 1)
    {
        var text = (selector ?? string.Empty).Trim();
        string? name = null;
        string? id = null;
        string? type = null;
        string? cls = null;

        var open = text.IndexOf('[');
        var head = open < 0 ? text : text[..open].Trim();
        if (head.Length > 0)
        {
            type = head;
        }

        var close = text.LastIndexOf(']');
        if (open >= 0 && close > open)
        {
            foreach (var clause in text[(open + 1)..close]
                         .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var split = clause.IndexOf('=');
                if (split <= 0)
                {
                    continue;
                }

                var key = clause[..split].Trim().ToLowerInvariant();
                var value = clause[(split + 1)..].Trim().Trim('\'', '"');
                switch (key)
                {
                    case "name":
                        name = value;
                        break;
                    case "automationid":
                        id = value;
                        break;
                    case "controltype":
                    case "type":
                        type = value;
                        break;
                    case "class":
                    case "classname":
                        cls = value;
                        break;
                }
            }
        }

        var title = (windowTitle ?? string.Empty).Trim();
        return new UiQuery(name, id, type, cls, title.Length == 0 ? null : title, index);
    }

    /// <summary>
    /// The selector text this query reads back as: the control type, then the parts that pin it
    /// down, in the order the engine reads them. Only a value the reader can give back unchanged
    /// is written, so a name holding a quote or a comma is left out rather than turning into a
    /// selector that quietly matches something else.
    /// </summary>
    public string ToSelector()
    {
        var parts = new List<string>();

        if (Readable(AutomationId))
        {
            parts.Add($"automationId='{AutomationId!.Trim()}'");
        }

        if (Readable(Name))
        {
            parts.Add($"name='{Name!.Trim()}'");
        }

        if (Readable(ClassName))
        {
            parts.Add($"className='{ClassName!.Trim()}'");
        }

        var type = ControlType?.Trim() ?? string.Empty;
        return parts.Count == 0 ? type : $"{type}[{string.Join(", ", parts)}]";
    }

    /// <summary>
    /// True when a value survives the trip: the reader splits a selector on commas and strips the
    /// quotes around a value, so anything carrying those would come back as a different string.
    /// </summary>
    private static bool Readable(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && !value.Contains(',')
           && !value.Contains('\'')
           && !value.Contains('"')
           && !value.Contains('[')
           && !value.Contains(']');
}

/// <summary>
/// One element of the desktop's UI Automation tree, read where the pointer was. This is what the
/// element picker needs to write a selector a macro can use again.
/// </summary>
public sealed record UiElementInfo(
    string Name,
    string AutomationId,
    string ControlType,
    string ClassName,
    ScreenPoint Location,
    ScreenSize Size,
    string WindowTitle)
{
    /// <summary>
    /// The selector a macro can use again: what kind of control it is, then the stablest thing
    /// the element offers — its automation id when it has one, and otherwise the name a person
    /// reads. A name follows the interface language, so it is the second choice.
    /// </summary>
    public string Selector
    {
        get
        {
            var identified = !string.IsNullOrWhiteSpace(AutomationId);
            var query = new UiQuery(
                Name: identified ? null : Trimmed(Name),
                AutomationId: identified ? AutomationId.Trim() : null,
                ControlType: Trimmed(ControlType));

            return query.ToSelector();
        }
    }

    private static string? Trimmed(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>What a command line gave back once it finished.</summary>
public sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>
/// How a program is to be started: what to run, where, and how it should come up. Gathered into
/// one thing rather than a row of loose arguments, because a call site should not have to be read
/// twice to tell one switch from another.
/// </summary>
public sealed record StartRequest(
    string FileName,
    string Arguments = "",
    string WorkingDirectory = "",
    bool Hidden = false,
    bool RunAsAdmin = false,
    IReadOnlyDictionary<string, string>? Environment = null);

/// <summary>
/// How a command line is to be run: what to run, where, how long it may take, and what it is
/// handed before it starts. <see cref="OnOutput"/> and <see cref="OnError"/> are for a caller
/// that wants each line while the program is still running; they are called on the thread that
/// asked for the run, and the whole of what was printed still comes back in the result.
/// </summary>
public sealed record CommandRequest(
    string FileName,
    string Arguments = "",
    string WorkingDirectory = "",
    int TimeoutMs = 30000,
    IReadOnlyDictionary<string, string>? Environment = null,
    string? StandardInput = null,
    Action<string>? OnOutput = null,
    Action<string>? OnError = null);

/// <summary>
/// How a window is picked out of the ones that are open. A title is what a person sees, but it
/// changes with the document and the language, so the program that owns the window and the class
/// it registered are the sturdier things to name.
/// </summary>
public enum WindowMatch
{
    /// <summary>Part of the window title, which is what a person reads off the title bar.</summary>
    Title,

    /// <summary>Part of the name of the program that owns the window, without ".exe".</summary>
    Process,

    /// <summary>The window class the owning program registered, such as "Notepad".</summary>
    ClassName,
}

/// <summary>
/// An open top-level window: what it is called, where it sits, and whether it is shrunk or
/// filling the screen. The handle is what the window actions are carried out on.
/// </summary>
public sealed record WindowInfo(
    long Handle,
    string Title,
    ScreenPoint Location,
    ScreenSize Size,
    bool Minimized,
    bool Maximized);
