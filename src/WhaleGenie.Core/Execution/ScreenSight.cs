using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// What looking at the screen means: the places a step searches, the picture taken of each, the beat
/// between two pictures, the colour of one pixel, and the marks a look leaves for whoever is
/// watching. Vision, OCR and the conditions a macro writes all read the screen through this one
/// class, so "where did this step look" has one answer rather than three.
/// </summary>
internal sealed class ScreenSight(IDeviceLayer devices, VariableStore variables, RunState run,
    StepProgress here, IRunLooks? looks, WindowFacts windows)
{
    /// <summary>The reader for the fields of one step.</summary>
    private StepArguments Args(ExecutableStep step)
        => new(step, variables, devices.Screen.RefreshMilliseconds);

    /// <summary>What a shape comes to, with the names in it worked out where they stand.</summary>
    private Value Read(ExpressionNode written) => Expression.Evaluate(written, variables);

    /// <summary>
    /// The corner a step's own numbers are counted from, and the window they belong to. Reading
    /// writing goes through this class rather than through a family, so the one question "where do
    /// this step's numbers start" is answered in one place for both.
    /// </summary>
    public (ScreenPoint? Corner, WindowInfo? Window) AnchorOf(ExecutableStep step)
        => windows.AnchorOf(step);

    /// <summary>
    /// The places to search: the whole screen unless the step names regions, each of which may be
    /// counted from a window's corner when the step says so. One row of the step's region is either
    /// a rectangle written out — which is what the region picker fills in — or the name of a
    /// variable, which is how a rectangle or a picture taken earlier gets to be searched.
    /// </summary>
    public IReadOnlyList<(ImageFrame Frame, ScreenPoint Origin)> SearchAreas(ExecutableStep step)
    {
        var rows = step.Rows("region");
        if (rows.Count == 0)
        {
            // With nowhere written down, the whole of whatever the step is about: the window its
            // numbers are counted from when it names one, and the desktop otherwise.
            var anchor = windows.AnchorOf(step);
            if (anchor.Window is { } window)
            {
                var client = devices.Windows.ClientOrigin(window.Handle);
                var size = devices.Windows.ClientSize(window.Handle);
                var whole = Shot(client.X, client.Y, size.Width, size.Height, window);
                return [(whole.Frame, whole.Origin)];
            }

            var desk = devices.Screen.Desktop.Bounds;
            var desktop = Shot(desk.Left, desk.Top, desk.Width, desk.Height);
            return [(desktop.Frame, desktop.Origin)];
        }

        var areas = new List<(ImageFrame, ScreenPoint)>();
        foreach (var row in rows)
        {
            areas.AddRange(Areas(step, row));
        }

        if (areas.Count == 0)
        {
            throw new StepFailure(RunKeys.BadRegion, Written(rows));
        }

        return areas;
    }

    /// <summary>What the regions of a step say, for a failure that has to show what it could not read.</summary>
    private static string Written(IReadOnlyList<IReadOnlyDictionary<string, ExpressionNode>> rows)
        => string.Join("; ", rows.Select(row =>
            string.Join(',', row.Values.Select(value => Formula.Speak(value)))));

    /// <summary>
    /// What one region of a step comes to. A rectangle written out says where it is; a region that
    /// names something looks the name up, which is how a picture an earlier Capture took is
    /// searched — the screen is grabbed once and looked at as often as the macro likes — and how a
    /// rectangle held in a variable gets to be used.
    /// </summary>
    private IEnumerable<(ImageFrame Frame, ScreenPoint Origin)> Areas(ExecutableStep step,
        IReadOnlyDictionary<string, ExpressionNode> row)
    {
        if (!row.TryGetValue("text", out var written))
        {
            yield return Rectangle(step, row);
            yield break;
        }

        // A picture taken earlier is looked up by the name the step says rather than through the
        // variables: what a variable holds is a note about the picture, and it is the picture
        // itself that is searched. The name is the shape's own, so nothing is taken off the front
        // of a word to find it.
        if (Name(written) is { } name && run.Images.TryGetValue(name, out var picture))
        {
            yield return (picture, new ScreenPoint(0, 0));
            yield break;
        }

        // A record is one rectangle with a name for each of its sides, which is what a hit hands
        // over: naming that record here is how "look again at what you just found" is written
        // without the macro spelling the four numbers out again.
        if (Read(written) is { IsRecord: true } record && Held(step, record) is { } held)
        {
            yield return held;
            yield break;
        }

        // Anything else is a rectangle, written out or held in a variable. It is only read for its
        // text rather than as a whole expression: the commas of a written rectangle would stop an
        // expression at its first number.
        var named = Read(written).AsText().Trim();

        // Several rectangles are separated by a semicolon or a line break, so a variable holding a
        // list of them goes on working the way it always did.
        foreach (var rectangle in named.Split([';', '\n', '\r'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return Written(step, rectangle);
        }
    }

    /// <summary>
    /// The rectangle a record stands for: its four sides by name, or the one written rectangle an
    /// action that hands its box over as text puts in it. Nothing when the record is neither, so a
    /// record that is not a rectangle is reported as a region nobody could read rather than as a
    /// failure of its own.
    /// </summary>
    private (ImageFrame Frame, ScreenPoint Origin)? Held(ExecutableStep step, Value record)
    {
        if (Sides(record) is { } sides)
        {
            // The record's own numbers are where the rectangle is on screen, the way a hit's are:
            // the step's coordinate origin moves the four numbers it is given as text, not this.
            // Which window the picture is read out of is a different question, and stays the
            // step's own answer.
            var shot = Shot(sides.Left, sides.Top, sides.Width, sides.Height,
                windows.AnchorOf(step).Window);
            return (shot.Frame, shot.Origin);
        }

        var box = record.Field("box")?.AsText().Trim() ?? string.Empty;
        return box.Length > 0 ? Written(step, box) : null;
    }

    /// <summary>One side of a rectangle held in a record, in whole screen pixels.</summary>
    private static int Whole(Value side) => (int)Math.Round(side.AsNumber());

    /// <summary>
    /// The four sides a record says a rectangle has, in whole screen pixels: the corner it starts at
    /// and how far it reaches from there. Every record that carries a rectangle carries all four of
    /// them, which is what lets a hit, a window or a picture taken earlier say where a step has to
    /// look without the macro spelling the numbers out again. Nothing when the record is not a
    /// rectangle, so a record of some other shape is refused by whoever asked for one. A rectangle
    /// with no width or no height is one pixel wide, the way a width nobody wrote is read.
    /// </summary>
    private static (int Left, int Top, int Width, int Height)? Sides(Value record)
    {
        if (record.Field(FieldNames.Left) is not { } left
            || record.Field(FieldNames.Top) is not { } top
            || record.Field(FieldNames.Width) is not { } width
            || record.Field(FieldNames.Height) is not { } height)
        {
            return null;
        }

        return (Whole(left), Whole(top), Math.Max(1, Whole(width)), Math.Max(1, Whole(height)));
    }

    /// <summary>One region written as four numbers, captured where on the screen it says.</summary>
    private (ImageFrame Frame, ScreenPoint Origin) Rectangle(ExecutableStep step,
        IReadOnlyDictionary<string, ExpressionNode> row)
    {
        var x = Cell(row, "x");
        var y = Cell(row, "y");
        var width = Cell(row, FieldNames.Width);
        var height = Cell(row, FieldNames.Height);
        if (width <= 0 || height <= 0)
        {
            throw new StepFailure(RunKeys.BadRegion, Written([row]));
        }

        return Capture(step, x, y, width, height);
    }

    /// <summary>What a row of a list parameter says in one column, or nothing when it has none.</summary>
    private static ExpressionNode Column(IReadOnlyDictionary<string, ExpressionNode> row,
        string column)
        => row.GetValueOrDefault(column) ?? Formula.Nothing;

    /// <summary>
    /// The name a shape is, when it is one reference and nothing else. A region that names a
    /// picture an earlier step took says that name rather than what the name holds.
    /// </summary>
    private static string? Name(ExpressionNode written)
        => written is VariableNode variable ? variable.Name : null;

    /// <summary>One number of a row of a list parameter, as its field was filled in.</summary>
    private int Cell(IReadOnlyDictionary<string, ExpressionNode> row, string column)
        => (int)Math.Round(Read(Column(row, column)).AsNumber());

    /// <summary>
    /// One number of a row that may be left empty, where empty means the run's own value: a row
    /// only has to spell out the hold or the gap that is not the one everywhere else.
    /// </summary>
    private int Cell(IReadOnlyDictionary<string, ExpressionNode> row, string column, int fallback)
        => Formula.IsNothing(Column(row, column))
            ? fallback
            : (int)Math.Round(Read(Column(row, column)).AsNumber());

    /// <summary>One written rectangle, such as one a variable holds.</summary>
    private (ImageFrame Frame, ScreenPoint Origin) Written(ExecutableStep step, string rectangle)
    {
        var parts = rectangle.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 4
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y)
            || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width)
            || !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height)
            || width <= 0
            || height <= 0)
        {
            throw new StepFailure(RunKeys.BadRegion, rectangle);
        }

        return Capture(step, x, y, width, height);
    }

    /// <summary>
    /// The picture taken of one rectangle. A window-anchored rectangle is counted from that
    /// window's corner, and the origin handed back is the one on screen, so the positions read out
    /// of the picture are pixels the rest of the macro can click on.
    /// </summary>
    private (ImageFrame Frame, ScreenPoint Origin) Capture(ExecutableStep step, int x, int y,
        int width, int height)
    {
        var anchor = windows.AnchorOf(step);
        var corner = ScreenMath.Placed(anchor.Corner, x, y);
        var shot = Shot(corner.X, corner.Y, width, height, anchor.Window);
        return (shot.Frame, shot.Origin);
    }

    /// <summary>
    /// One picture of the screen, taken the way this machine is set up to take one: whether the
    /// pixels come off the screen or out of the window itself is an answer about the machine rather
    /// than about the step, and so is how long a picture already taken stays worth reading again.
    /// The window is the one the caller's coordinates are counted from, or the one it names
    /// outright.
    /// </summary>
    public ScreenShot Shot(int x, int y, int width, int height, WindowInfo? window = null)
    {
        // Which window this is about to be read out of is asked for before anything is read, so a
        // debugger can put a frame there while the reading is going on rather than after it.
        Reading(window);

        var method = window is null
            ? devices.Capture.ForScreen()
            : devices.Capture.ForWindow();

        var source = window?.Handle ?? 0;
        var age = devices.Frames.CacheMilliseconds;
        if (age <= 0)
        {
            // Nothing already taken is old enough to do, so the rectangle asked for is the rectangle
            // read: this is the reading a macro has always done, at the cost it has always had.
            var exact = devices.Screen.Capture(new ScreenCaptureRequest(x, y, width, height,
                method, source));
            Taken(window, exact.Method);
            return exact;
        }

        if (devices.Frames.Recall(source, method, age, out var kept)
            && Covers(kept.Frame, kept.Origin, x, y, width, height))
        {
            Taken(window, kept.Method);
            return Cut(kept.Frame, kept.Origin, x, y, width, height, kept.Method);
        }

        // The whole of what the step is reading, so that the other regions of the same step — and
        // the look after it — are served without reading the screen again: asking for one rectangle
        // costs more than that rectangle, and asking for the whole source is what makes the next
        // look free. A picture kept for less than one beat can only serve a look taken at the same
        // moment, so a short keep reads the rectangle asked for instead of paying for a whole screen
        // it would never hand out again.
        var taken = age >= FrameGap()
            ? Whole(method, window)
            : devices.Screen.Capture(new ScreenCaptureRequest(x, y, width, height, method, source));

        devices.Frames.Keep(source, method, taken);
        Taken(window, taken.Method);
        return Cut(taken.Frame, taken.Origin, x, y, width, height, taken.Method);
    }

    /// <summary>The whole of what a step reads from: the window, or the screen.</summary>
    private ScreenShot Whole(CaptureWay method, WindowInfo? window)
    {
        if (window is null)
        {
            var desk = devices.Screen.Desktop.Bounds;
            return devices.Screen.Capture(
                new ScreenCaptureRequest(desk.Left, desk.Top, desk.Width, desk.Height, method));
        }

        return devices.Screen.Capture(new ScreenCaptureRequest(window.Location.X, window.Location.Y,
            window.Size.Width, window.Size.Height, method, window.Handle));
    }

    /// <summary>True when a picture taken whole covers the rectangle a step is asking for.</summary>
    private static bool Covers(ImageFrame frame, ScreenPoint origin, int x, int y, int width,
        int height)
        => x >= origin.X && y >= origin.Y
           && x + width <= origin.X + frame.Width && y + height <= origin.Y + frame.Height;

    /// <summary>
    /// The part of a picture taken whole that a step asked for. A rectangle reaching past the
    /// picture is cut down to what is there, the way a window read as itself is, and one altogether
    /// outside the picture is nothing to look at.
    /// </summary>
    private static ScreenShot Cut(ImageFrame whole, ScreenPoint origin, int x, int y, int width,
        int height, CaptureWay method)
    {
        if (origin.X == x && origin.Y == y && whole.Width == width && whole.Height == height)
        {
            return new ScreenShot(whole, origin, method);
        }

        var left = Math.Max(0, x - origin.X);
        var top = Math.Max(0, y - origin.Y);
        var right = Math.Min(whole.Width, x - origin.X + width);
        var bottom = Math.Min(whole.Height, y - origin.Y + height);
        if (right <= left || bottom <= top)
        {
            throw new StepFailure(RunKeys.EmptyRegion, $"{x},{y} {width}x{height}");
        }

        var kept = new byte[(long)(right - left) * (bottom - top) * 4];
        for (var row = 0; row < bottom - top; row++)
        {
            Array.Copy(whole.Bgra, ((long)(top + row) * whole.Width + left) * 4, kept,
                (long)row * (right - left) * 4, (right - left) * 4);
        }

        return new ScreenShot(new ImageFrame(right - left, bottom - top, kept),
            new ScreenPoint(origin.X + left, origin.Y + top), method);
    }

    /// <summary>
    /// Notes what the picture a step is about to be given was taken of. Every look passes through
    /// here, whether it read the screen or read a picture already in hand, so a step that came out
    /// wrong can be asked what it was really looking at.
    /// </summary>
    private void Taken(WindowInfo? window, CaptureWay method)
    {
        here.TakenWindow = window?.Title ?? string.Empty;
        here.TakenMethod = method;
    }

    /// <summary>Tells whoever is watching which window the next picture comes out of.</summary>
    private void Reading(WindowInfo? window) => looks?.Reading(window);

    /// <summary>
    /// Waits for this look's turn. Every step that reads the screen comes through here, so the beat
    /// is neither a step's nor a run's but the machine's: ten steps in one loop — or ten macros
    /// recognising pictures at the same moment — share it instead of each taking a picture of its
    /// own.
    /// </summary>
    public async Task Gate(CancellationToken token)
    {
        var left = devices.Frames.Turn(FrameGap());
        if (left > 0)
        {
            await Pacing.Pause(left, token);
        }
    }

    /// <summary>
    /// The shortest wait between two looks, from what the settings window stores: the cap the person
    /// set, or the display's own rate when that cap is absent or higher. A picture that has not been
    /// drawn again cannot differ from the one already read, so anything above the display's rate
    /// would only read the same frame twice at the cost of reading it.
    /// </summary>
    public int FrameGap()
    {
        var hertz = Math.Max(1, devices.Screen.RefreshHertz);
        var asked = devices.Frames.MaxFramesPerSecond;
        if (asked <= 0 || asked >= hertz)
        {
            return Math.Max(1, devices.Screen.RefreshMilliseconds);
        }

        return Math.Max(1, (int)Math.Ceiling(1000.0 / asked));
    }

    /// <summary>
    /// Runs one step that looks at the screen and hands over what it saw, whatever came of it: the
    /// look of a step that found nothing is the one worth having.
    /// </summary>
    public void Watching(Action body)
    {
        here.Saw = null;
        try
        {
            body();
        }
        finally
        {
            Show();
        }
    }

    /// <summary>The same, for a step that has to wait for what it is looking at.</summary>
    public async Task Watching(Func<Task> body)
    {
        here.Saw = null;
        try
        {
            await body();
        }
        finally
        {
            Show();
        }
    }

    /// <summary>Hands the look over to whoever is watching, and forgets it.</summary>
    private void Show()
    {
        var look = here.Saw;
        here.Saw = null;
        if (looks is null || look is null)
        {
            return;
        }

        looks.Look(look);
    }

    /// <summary>
    /// How the marks are built, and what sort of looking this was. Nothing is built when nobody is
    /// watching: a picture of every step of a run nobody is looking at is work for nothing, and a
    /// step that waits would build one every two hundred milliseconds.
    /// </summary>
    public void Saw(ExecutableStep step, LookKind kind, ImageFrame frame, ScreenPoint origin,
        IReadOnlyList<LookBox> boxes, int chosen, string looking = "", ImageFrame? needle = null,
        string? note = null, double? minimum = null, int picture = 0, int pictures = 0)
    {
        if (looks is null)
        {
            return;
        }

        here.Saw = new StepLook
        {
            StepId = step.Id,
            StepType = step.Type,
            Kind = kind,
            Frame = frame,
            Origin = origin,
            Boxes = boxes,
            ChosenIndex = chosen,
            Looking = looking,
            Needle = needle,
            Note = note,
            Minimum = minimum,
            PictureNumber = picture,
            PictureCount = pictures,
            Source = here.TakenWindow,
            Method = here.TakenMethod ?? CaptureWay.Auto,
        };
    }

    /// <summary>
    /// What a step that looked for something saw: the areas it searched, every place the thing was
    /// found, and the one it went with. Several areas are put together into one picture so that
    /// every mark has somewhere to sit.
    /// </summary>
    public void SawSearch(ExecutableStep step, LookKind kind,
        IReadOnlyList<(ImageFrame Frame, ScreenPoint Origin)> areas,
        IReadOnlyList<(ImageMatch Match, string? Label)> found, int chosen, string looking = "",
        ImageFrame? needle = null, double? minimum = null, int picture = 0, int pictures = 0,
        string? note = null)
    {
        if (looks is null)
        {
            return;
        }

        var (frame, origin) = Picture(areas);
        Saw(step, kind, frame, origin, Marks(areas, found, chosen),
            chosen == 0 ? 0 : areas.Count + chosen, looking, needle, note, minimum, picture,
            pictures);
    }

    /// <summary>
    /// Adds where a step has just acted to the look it took to get there, so a step that clicked
    /// the wrong place can be seen rather than guessed at.
    /// </summary>
    public void Aim(ScreenPoint point)
    {
        if (looks is null || here.Saw is null)
        {
            return;
        }

        var boxes = new List<LookBox>(here.Saw.Boxes)
        {
            new(new ImageMatch(1, point, new ScreenSize(1, 1)), LookRole.Target),
        };

        here.Saw = here.Saw with { Boxes = boxes, ChosenIndex = boxes.Count };
    }

    /// <summary>
    /// A look at a single pixel: a square of screen around it with the pixel itself marked, which
    /// is what a step watching for a colour has to show for itself.
    /// </summary>
    public void SawPixel(ExecutableStep step, ScreenPoint point, string looking,
        WindowInfo? window)
    {
        if (looks is null)
        {
            return;
        }

        var desk = devices.Screen.Desktop.Bounds;
        var left = Math.Clamp(point.X - Neighbourhood, desk.Left, Math.Max(desk.Left, desk.Right - 1));
        var top = Math.Clamp(point.Y - Neighbourhood, desk.Top, Math.Max(desk.Top, desk.Bottom - 1));
        var right = Math.Clamp(point.X + Neighbourhood + 1, left + 1, desk.Right);
        var bottom = Math.Clamp(point.Y + Neighbourhood + 1, top + 1, desk.Bottom);

        var shot = Shot(left, top, right - left, bottom - top, window);
        Saw(step, LookKind.Pixel, shot.Frame, shot.Origin,
            [
                new(new ImageMatch(1, shot.Origin,
                    new ScreenSize(shot.Frame.Width, shot.Frame.Height)), LookRole.Area),
                new(new ImageMatch(1, point, new ScreenSize(1, 1)), LookRole.Target),
            ],
            2, looking);
    }

    /// <summary>How far around a watched pixel the look reaches, so the pixel has some context.</summary>
    private const int Neighbourhood = 60;

    /// <summary>The marks for a look: where it looked, and everything that was found there.</summary>
    public static List<LookBox> Marks(
        IReadOnlyList<(ImageFrame Frame, ScreenPoint Origin)> areas,
        IReadOnlyList<(ImageMatch Match, string? Label)> found, int chosen)
    {
        var marks = new List<LookBox>();
        foreach (var (area, origin) in areas)
        {
            marks.Add(new LookBox(
                new ImageMatch(0, origin, new ScreenSize(area.Width, area.Height)), LookRole.Area));
        }

        for (var index = 0; index < found.Count; index++)
        {
            marks.Add(new LookBox(
                found[index].Match,
                index + 1 == chosen ? LookRole.Hit : LookRole.Candidate,
                found[index].Label));
        }

        return marks;
    }

    /// <summary>
    /// The picture to draw a step's marks on: the single area it looked at, or the whole of what
    /// it looked at when there are several. Several are laid out of the areas themselves rather
    /// than read off the screen again — where the areas came from is not always the screen, and a
    /// step that reads a window it does not cover would otherwise be drawn over a picture of
    /// whatever is on top of it.
    /// </summary>
    private static (ImageFrame Frame, ScreenPoint Origin) Picture(
        IReadOnlyList<(ImageFrame Frame, ScreenPoint Origin)> areas)
    {
        if (areas.Count == 1)
        {
            return areas[0];
        }

        var left = areas.Min(area => area.Origin.X);
        var top = areas.Min(area => area.Origin.Y);
        var width = Math.Max(1, areas.Max(area => area.Origin.X + area.Frame.Width) - left);
        var height = Math.Max(1, areas.Max(area => area.Origin.Y + area.Frame.Height) - top);
        var pixels = new byte[(long)width * height * 4];
        foreach (var (frame, origin) in areas)
        {
            for (var row = 0; row < frame.Height; row++)
            {
                Array.Copy(frame.Bgra, (long)row * frame.Width * 4, pixels,
                    ((long)(origin.Y - top + row) * width + (origin.X - left)) * 4,
                    (long)frame.Width * 4);
            }
        }

        return (new ImageFrame(width, height, pixels), new ScreenPoint(left, top));
    }

    /// <summary>
    /// The colour of one pixel. A pixel the step counts from the screen is read straight off the
    /// screen, which is a fraction of the cost of a picture; one it counts from a window is read
    /// out of a picture of that window, so a window another window covers still gives its own
    /// colour rather than the covering window's.
    /// </summary>
    public PixelColor PixelAt(ExecutableStep step, ScreenPoint point, WindowInfo? window)
    {
        if (window is null)
        {
            // A pixel of the desktop is read straight off it rather than out of a picture, so this
            // is the one reading that has to say for itself that it is not reading a window.
            Reading(null);
            return devices.Screen.PixelAt(point.X, point.Y);
        }

        var shot = Shot(point.X, point.Y, 1, 1, window);
        if (shot.Origin != point)
        {
            // The picture came back from somewhere else, which means the window does not reach
            // that far: there is no such pixel, and reading a neighbour's colour would be a lie.
            throw new StepFailure(RunKeys.PixelOutsideScreen, $"{point.X},{point.Y}");
        }

        return shot.Frame[0, 0];
    }
}
