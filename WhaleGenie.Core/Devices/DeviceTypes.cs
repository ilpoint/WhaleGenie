using System;
using System.Globalization;

namespace WhaleGenie.Core.Devices;

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

    /// <summary>
    /// How much of the picture moved between two frames of the same area: the share of pixels
    /// whose colour is more than <paramref name="tolerancePercent"/> away from what it was, from 0
    /// for a picture that did not move at all to 1 for one that changed everywhere. Two frames of
    /// different sizes are nothing like each other, so that is 1.
    /// </summary>
    /// <remarks>
    /// The whole point of a tolerance is that a cursor blinking or a video playing quietly is not
    /// what a macro means by "the screen moved", so a count of every pixel that differs at all
    /// would answer the wrong question. What is counted is pixels that differ by more than the
    /// caller is willing to call the same colour.
    /// </remarks>
    public static double ChangedShare(ImageFrame before, ImageFrame after, double tolerancePercent)
    {
        if (before.Width != after.Width || before.Height != after.Height)
        {
            return 1;
        }

        var pixels = (long)before.Width * before.Height;
        if (pixels <= 0)
        {
            return 0;
        }

        // Compared squared, which is the same question without the square root: how far apart the
        // colour channels are, against how far the caller allows them to be.
        var left = before.Bgra;
        var right = after.Bgra;
        var limit = Math.Max(0, tolerancePercent) / 100 * 255;
        var allowed = 3 * limit * limit;

        long changed = 0;
        for (long offset = 0; offset + 3 < left.Length && offset + 3 < right.Length; offset += 4)
        {
            double blue = left[offset] - right[offset];
            double green = left[offset + 1] - right[offset + 1];
            double red = left[offset + 2] - right[offset + 2];
            if ((blue * blue) + (green * green) + (red * red) > allowed)
            {
                changed++;
            }
        }

        return (double)changed / pixels;
    }
}

/// <summary>
/// How a picture of part of the screen is taken. The desktop can be read the way a macro always
/// has; a window can be asked to draw itself, or read through the same machinery that composes it
/// on screen, which is what still finds a game that another window is covering. The two are not
/// interchangeable: the first is cheapest and needs nothing of the graphics card, the second works
/// when nothing of the window is visible.
/// </summary>
public enum CaptureMethod
{
    /// <summary>
    /// Whatever suits the source: the desktop through GDI, a window through graphics capture and
    /// then, if this machine has none, by asking the window to draw itself.
    /// </summary>
    Auto,

    /// <summary>The desktop, copied out of it by GDI. This is what a macro has always done.</summary>
    Gdi,

    /// <summary>A window, asked to draw itself into a picture of our own.</summary>
    PrintWindow,

    /// <summary>A window, read the way the screen shows it, so a covering window changes nothing.</summary>
    GraphicsCapture,

    /// <summary>The desktop, read the way the screen shows it.</summary>
    GraphicsCaptureDesktop,
}

/// <summary>
/// The names the ways of taking a picture go by, in the one place they are spelled: a step's field
/// holds one of them, a result says one back, and a look says which one it was really taken with.
/// </summary>
public static class CaptureMethodNames
{
    /// <summary>The ways a region of the screen can be asked for, in the order they are offered.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        "auto", "gdi", "printWindow", "graphicsCapture", "graphicsCaptureDesktop",
    ];

    /// <summary>
    /// What one way is written as, or empty for the automatic choice — which is a question rather
    /// than a way of taking a picture, and is never the answer to one.
    /// </summary>
    public static string Written(CaptureMethod method) => method switch
    {
        CaptureMethod.Gdi => "gdi",
        CaptureMethod.PrintWindow => "printWindow",
        CaptureMethod.GraphicsCapture => "graphicsCapture",
        CaptureMethod.GraphicsCaptureDesktop => "graphicsCaptureDesktop",
        _ => string.Empty,
    };

    /// <summary>
    /// The way a written name stands for. Anything that is not one of them is the automatic choice,
    /// so a half-written step looks at something rather than at nothing.
    /// </summary>
    public static CaptureMethod Read(string? written) => (written ?? string.Empty).Trim() switch
    {
        "gdi" => CaptureMethod.Gdi,
        "printwindow" => CaptureMethod.PrintWindow,
        "graphicscapture" => CaptureMethod.GraphicsCapture,
        "graphicscapturedesktop" => CaptureMethod.GraphicsCaptureDesktop,
        _ => CaptureMethod.Auto,
    };
}

/// <summary>
/// A picture that was taken, and where its top left corner sits on the screen. The two travel
/// together because the picture is not always the rectangle that was asked for — a window read
/// through graphics capture is its own size, at its own corner — and everything the run does with
/// the picture afterwards is in screen pixels. Which way it was taken is beside them, because
/// "automatic" is a question rather than an answer, and a step that came out wrong is worth being
/// able to ask what it was really read with.
/// </summary>
public readonly record struct ScreenShot(ImageFrame Frame, ScreenPoint Origin, CaptureMethod Method);

/// <summary>
/// One region to read, and where its pixels should come from. The rectangle is in screen pixels,
/// the way every other coordinate a macro writes is; naming a window only says where to reach for
/// those pixels, and turning the two into each other is the device's business rather than the
/// macro's.
/// </summary>
public sealed record ScreenCaptureRequest(
    int X,
    int Y,
    int Width,
    int Height,
    CaptureMethod Method = CaptureMethod.Auto,
    long Window = 0)
{
    /// <summary>True when the pixels are to come from a window rather than from the desktop.</summary>
    public bool IsWindow => Window != 0;

    /// <summary>The same rectangle, moved, which is how a search area is placed inside a picture.</summary>
    public ScreenCaptureRequest At(int x, int y) => this with { X = x, Y = y };
}

/// <summary>Where a reference image was found, and how well it matched.</summary>
public sealed record ImageMatch(double Score, ScreenPoint Location, ScreenSize Size)
{
    /// <summary>The middle of the match, which is what a click aims at.</summary>
    public ScreenPoint Center => new(Location.X + (Size.Width / 2), Location.Y + (Size.Height / 2));
}

/// <summary>
/// How a reference picture is looked for. The three ways of comparing two pictures pixel for pixel
/// and the one way of pairing up their features are one list rather than two settings, because
/// they answer one question — and because a step that pairs up features has no use for which way
/// pixels are compared, so offering both would leave one of them doing nothing.
/// </summary>
public enum MatchAlgorithm
{
    /// <summary>
    /// Normalised correlation: the area may come out brighter or darker overall and it still
    /// matches, which is what a macro usually wants.
    /// </summary>
    Normed,

    /// <summary>
    /// Normalised cross correlation, which does not take the brightness out first: steadier for a
    /// very small reference picture, where there is little to average.
    /// </summary>
    Correlated,

    /// <summary>
    /// Normalised square difference read the other way round: what it wants is a pixel for pixel
    /// copy. A flat coloured icon or a screenshot of a dialog is the case for it.
    /// </summary>
    Difference,

    /// <summary>
    /// Pairing up the features found in both pictures: slower, and it still finds the thing when
    /// it is drawn at another size or has something small changed about it.
    /// </summary>
    Feature,
}

/// <summary>What order the hits are handed back in, which is what "the third one" means.</summary>
public enum MatchOrder
{
    /// <summary>Down the screen first and then across, the order a person counts them in.</summary>
    Reading,

    /// <summary>The surest first, wherever on the screen it is.</summary>
    Score,

    /// <summary>
    /// The biggest box first. A picture looked for by features comes back at whatever size it was
    /// drawn at, and a reading of writing can be a whole line or one word, so the biggest one is
    /// how a step says "the whole banner, not a piece of it".
    /// </summary>
    Area,

    /// <summary>
    /// Shuffled, for a macro that must not always take the same one of several.
    /// </summary>
    Random,
}

/// <summary>One search asked for: how it is done, how sure it has to be, and what to leave out.</summary>
public sealed record VisionQuery(
    MatchAlgorithm Algorithm = MatchAlgorithm.Normed,
    double ConfidencePercent = 90,
    int MinFeatures = 6,
    PixelColor? Skip = null,
    int Limit = 1);

/// <summary>
/// One piece of text found on screen. The confidence is the reading model's own score rather than
/// a percentage: it is the average, over the characters it read, of how far ahead the model's best
/// guess was, so a bigger number means a surer reading but there is no range it has to sit in.
/// </summary>
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
/// <summary>
/// What a table handed back: the names of its columns, and the rows of data under them. A grid
/// keeps the names in a strip of its own rather than in a row, which is why they come back
/// separately — a macro that has to know which column holds what reads them, and one that only
/// wants the data is not handed a row that is not data.
/// </summary>
public sealed record UiTable(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows)
{
    /// <summary>A table with nothing in it.</summary>
    public static UiTable Empty { get; } = new([], []);
}

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
/// The things a person can do to a machine from the Start menu's power button, which are also the
/// things a macro that runs overnight needs to be able to do to it.
/// </summary>
public enum PowerAction
{
    /// <summary>Locks the screen, leaving everything running behind it.</summary>
    Lock,

    /// <summary>Turns the screen off. Any key or movement of the mouse turns it back on.</summary>
    MonitorOff,

    /// <summary>Signs the current user out, closing what is open.</summary>
    SignOut,

    /// <summary>Suspends the machine, keeping what is open in memory.</summary>
    Sleep,

    /// <summary>Writes what is open to disk and turns the machine off.</summary>
    Hibernate,

    /// <summary>Closes what is open and starts the machine again.</summary>
    Restart,

    /// <summary>Closes what is open and turns the machine off.</summary>
    ShutDown,

    /// <summary>Cancels a restart or shutdown that is still counting down.</summary>
    AbortShutdown,
}

/// <summary>
/// The sounds a machine plays for the things that happen to it, which are also what a macro that
/// has finished — or gone wrong — can play to say so while nobody is looking at the screen.
/// Which sound each of these is, and whether any of them is heard at all, is the machine's own
/// sound scheme: WhaleGenie asks for an event, not for a file.
/// </summary>
public enum SoundKind
{
    /// <summary>Whatever this machine plays when nothing more particular is asked for.</summary>
    Default,

    /// <summary>The sound for an ordinary notice.</summary>
    Information,

    /// <summary>The sound for something worth looking at.</summary>
    Warning,

    /// <summary>The sound for something that went wrong.</summary>
    Error,

    /// <summary>The sound for a question.</summary>
    Question,
}

/// <summary>
/// How a notification looks when it appears: an ordinary note, something worth looking at, or
/// something that went wrong. The three correspond to the machine's own notification pictures, so
/// a macro picks a meaning rather than a drawing.
/// </summary>
public enum NotificationKind
{
    /// <summary>An ordinary note.</summary>
    Information,

    /// <summary>Something worth looking at.</summary>
    Warning,

    /// <summary>Something that went wrong.</summary>
    Error,
}

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
/// <see cref="OutputEncoding"/> names the code page the program writes in, or is left alone for
/// this machine's own; reading a Chinese Windows program's output as UTF-8 turns it into question
/// marks, so the two ends have to agree on which it is.
/// </summary>
public sealed record CommandRequest(
    string FileName,
    string Arguments = "",
    string WorkingDirectory = "",
    int TimeoutMs = 30000,
    IReadOnlyDictionary<string, string>? Environment = null,
    string? StandardInput = null,
    Action<string>? OnOutput = null,
    Action<string>? OnError = null,
    string? OutputEncoding = null);

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
/// How the text a step wrote is held up against the part of the window it named. "Contains" is what
/// a person means when they type part of a title; a regular expression is for the windows whose
/// titles carry something that changes from run to run.
/// </summary>
public enum WindowCompare
{
    /// <summary>The part holds the text, ignoring case.</summary>
    Contains,

    /// <summary>The part begins with the text, ignoring case.</summary>
    StartsWith,

    /// <summary>The text is a regular expression matched against the part, ignoring case.</summary>
    Regex,
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
