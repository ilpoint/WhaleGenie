using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.TestSupport;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// A run as one action family sees it: the fake devices, the results and log lines recorded rather
/// than performed, and the window rule read the way a step wrote it. A family test uses this
/// instead of building a runner, which is the point of the split.
/// </summary>
internal sealed class FakeRunContext : IRunContext, IRunPoints, IScreenReads, IScreenShots
{
    private readonly VariableStore _variables = new();

    public FakeDeviceLayer Machine { get; } = new();

    public Dictionary<string, ImageFrame> Images { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, Value> Written { get; } = new(StringComparer.Ordinal);

    public List<(LogLevel Level, string Key)> Logged { get; } = [];

    public List<(LookKind Kind, ImageFrame Frame)> Shown { get; } = [];

    IDeviceLayer IRunContext.Devices => Machine;

    IDictionary<string, ImageFrame> IRunContext.Images => Images;

    VariableStore IRunContext.Variables => _variables;

    string IRunContext.PathOf(ExecutableStep step, string parameter)
        => ((IRunContext)this).Args(step).ValueOf(parameter).AsText();

    IRunPoints IRunContext.Points => this;

    IScreenReads IRunContext.Reads => this;

    /// <summary>The searches a family under test handed to whoever is watching.</summary>
    public List<(string Wanted, int Chosen, int Found)> Searches { get; } = [];

    IReadOnlyList<(ImageFrame Frame, ScreenPoint Origin)> IScreenReads.Areas(
        ExecutableStep step)
    {
        var desk = Machine.Screen.Desktop.Bounds;
        var shot = Machine.Screen.Capture(
            new ScreenCaptureRequest(desk.Left, desk.Top, desk.Width, desk.Height));
        return [(shot.Frame, shot.Origin)];
    }

    (IReadOnlyList<TextSpan> Spans, IReadOnlyList<(ImageFrame Frame, ScreenPoint Origin)> Areas)
        IScreenReads.Read(ExecutableStep step, CancellationToken token)
    {
        var frame = Picture();
        var area = (frame, new ScreenPoint(0, 0));
        return (Machine.Ocr.Recognize(frame), [area]);
    }

    (List<TextSpan> Hits, int Chosen) IScreenReads.Counting(ExecutableStep step,
        IReadOnlyList<TextSpan> readings, string wanted, string mode)
    {
        var hits = readings
            .Where(span => TextCompare.Matches(span.Text, wanted, TextCompare.Contains))
            .ToList();
        return (hits, hits.Count > 0 ? 1 : 0);
    }

    Task IScreenReads.WaitForScreen(CancellationToken token) => Task.CompletedTask;

    void IScreenReads.Showing(Action body) => body();

    Task IScreenReads.Showing(Func<Task> body) => body();

    void IScreenReads.SawSearch(ExecutableStep step, LookKind kind,
        IReadOnlyList<(ImageFrame Frame, ScreenPoint Origin)> areas,
        IReadOnlyList<(ImageMatch Match, string? Label)> found, int chosen, string wanted,
        ImageFrame? needle, double? minimum, int picture, int pictures, string? note)
        => Searches.Add((wanted, chosen, found.Count));

    void IScreenReads.ClickAt(ExecutableStep step, ScreenPoint at)
        => Machine.Input.Click("left", at.X, at.Y, 1, 0);

    IScreenShots IRunContext.Shots => this;

    IReadOnlyList<(string Name, ImageFrame Frame)> IRunContext.Pictures(ExecutableStep step)
        => [.. step.Rows("images").Select(row => (((IRunContext)this).Word(row, "image"),
            Picture()))];

    (ScreenPoint Corner, WindowInfo? Window) IScreenShots.Spot(ExecutableStep step)
    {
        var args = ((IRunContext)this).Args(step);
        return (new ScreenPoint(args.Number("x"), args.Number("y")), null);
    }

    ScreenShot IScreenShots.Take(int x, int y, int width, int height, WindowInfo? window)
        => Machine.Screen.Capture(new ScreenCaptureRequest(x, y, width, height, window is null
            ? Machine.Screen.Ways.ForScreen()
            : Machine.Screen.Ways.ForWindow(), window?.Handle ?? 0));

    PixelColor IScreenShots.At(ExecutableStep step, ScreenPoint point, WindowInfo? window)
        => Machine.Screen.PixelAt(point.X, point.Y);

    void IScreenShots.SawPixel(ExecutableStep step, ScreenPoint point, string looking,
        WindowInfo? window) => Shown.Add((LookKind.Pixel, Picture()));

    void IScreenShots.Show(ExecutableStep step, LookKind kind, ImageFrame frame,
        ScreenPoint origin, IReadOnlyList<LookBox> boxes, int chosen, string looking)
        => Shown.Add((kind, frame));

    void IScreenShots.GiveTaken(ExecutableStep step)
    {
        Written["window"] = Value.FromText(string.Empty);
        Written["method"] = Value.FromText(string.Empty);
    }

    private static ImageFrame Picture() => new(10, 10, new byte[400]);

    StepArguments IRunContext.Args(ExecutableStep step) => new(step, _variables, 16);

    ResolvedPoint IRunPoints.Point(ExecutableStep step, string xName, string yName)
    {
        var args = ((IRunContext)this).Args(step);
        var point = new ScreenPoint(args.Number(xName), args.Number(yName));
        return new ResolvedPoint(point, point, null);
    }

    ResolvedPoint IRunPoints.MovePlace(ExecutableStep step)
        => ((IRunPoints)this).Point(step, "x", "y");

    ResolvedPoint IRunPoints.DragPlace(ExecutableStep step, string end,
        (ScreenPoint? Corner, WindowInfo? Window) anchor)
        => ((IRunPoints)this).Point(step, end + "X", end + "Y");

    (ScreenPoint? Corner, WindowInfo? Window) IRunPoints.AnchorOf(ExecutableStep step)
        => (null, null);

    bool IRunPoints.PostsMessages(ExecutableStep step) => false;

    ScreenPoint IRunPoints.Scatter(ExecutableStep step, ScreenPoint point) => point;

    void IRunPoints.GivePoints(ExecutableStep step, ScreenPoint screen, ScreenPoint? window,
        string screenKey, string windowKey)
    {
        if (((IRunContext)this).Wants(step, screenKey))
        {
            Written[screenKey] = PointRecord(screen, "screen");
        }

        if (window is { } client && ((IRunContext)this).Wants(step, windowKey))
        {
            Written[windowKey] = PointRecord(client, "window");
        }
    }

    void IRunPoints.GivePointAt(ExecutableStep step, ScreenPoint screen, WindowInfo? window)
        => ((IRunPoints)this).GivePoints(step, screen, null);

    private static Value PointRecord(ScreenPoint point, string basis)
        => Value.FromRecord(
        [
            (FieldNames.X, Value.FromNumber(point.X)),
            (FieldNames.Y, Value.FromNumber(point.Y)),
            (FieldNames.Basis, Value.FromText(basis)),
        ]);

    bool IRunContext.Flag(ExecutableStep step, string name, bool fallback)
    {
        var text = step.Text(name).Trim();
        return text.Length == 0
            ? fallback
            : !string.Equals(text, "false", StringComparison.OrdinalIgnoreCase);
    }

    bool IRunContext.CaseMatches(ExecutableStep step, Value value, string wanted, string mode)
    {
        var text = value.AsText();
        return mode.Trim().ToLowerInvariant() switch
        {
            "notequals" => !ValueRules.Same(value, Value.FromText(wanted)),
            "contains" or "startswith" or "endswith" => TextCompare.Matches(text, wanted, mode),
            "notcontains" => !TextCompare.Matches(text, wanted, TextCompare.Contains),
            "greaterthan" => Number(text).CompareTo(Number(wanted)) > 0,
            "greaterorequal" => Number(text).CompareTo(Number(wanted)) >= 0,
            "lessthan" => Number(text).CompareTo(Number(wanted)) < 0,
            "lessorequal" => Number(text).CompareTo(Number(wanted)) <= 0,
            "regex" => TextCompare.Matches(text, wanted, TextCompare.Regex),
            _ => ValueRules.Same(value, Value.FromText(wanted))
                 || TextCompare.Matches(text, wanted, TextCompare.Exactly),
        };

        static double Number(string written)
            => double.TryParse(written, NumberStyles.Any, CultureInfo.InvariantCulture, out var number)
                ? number
                : 0;
    }

    bool IRunContext.Wants(ExecutableStep step, string key) => step.Return(key).Length > 0;

    WindowRule IRunContext.WindowRuleOf(ExecutableStep step)
        => WindowRule.TryParse(step.Text("window"), out var rule) ? rule : WindowRule.Empty;

    WindowInfo? IRunContext.Lookup(ExecutableStep step)
    {
        var rule = ((IRunContext)this).WindowRuleOf(step);
        var windows = (IWindowDevice)Machine.Window;
        return rule.HasTarget
            ? windows.Find(rule.MatchValue, rule.Reading.Match, rule.Reading.Compare,
                rule.Options)
            : windows.Foreground();
    }

    int IRunContext.Index(ExecutableStep step, string name, int fallback)
    {
        var args = ((IRunContext)this).Args(step);
        var text = args.Text(name).Trim();
        return text.Length == 0 ? fallback : Math.Clamp(args.Number(name), 1, RunLimits.Matches);
    }

    WindowInfo IRunContext.Locate(ExecutableStep step)
        => ((IRunContext)this).Lookup(step)
           ?? throw new StepFailure(RunKeys.WindowNotFound, step.Text("window"));

    (int Left, int Top, int Width, int Height)? IRunContext.RectOf(ExecutableStep step, string name)
    {
        var value = ((IRunContext)this).Args(step).ValueOf(name);
        if (value.IsNull)
        {
            return null;
        }

        int Side(string side)
            => (int)(value.Field(side)?.AsNumber()
                        ?? throw new StepFailure(RunKeys.BadRegion, step.Text(name).Trim()));

        return value is { IsRecord: true }
            ? (Side("left"), Side("top"), Side("width"), Side("height"))
            : throw new StepFailure(RunKeys.BadRegion, step.Text(name).Trim());
    }

    string IRunContext.Button(ExecutableStep step)
    {
        var button = step.Text("button").Trim();
        return button.Length == 0 ? "left" : button;
    }

    IReadOnlyList<string> IRunContext.Keys(ExecutableStep step)
        => [.. step.Text("keys").Split('+', StringSplitOptions.RemoveEmptyEntries
            | StringSplitOptions.TrimEntries)];

    int IRunContext.Pace(int milliseconds) => Math.Max(0, milliseconds);

    int IRunContext.Number(IReadOnlyDictionary<string, ExpressionNode> row, string column,
        int fallback)
        => row.TryGetValue(column, out var written) && !Formula.IsNothing(written)
            ? (int)Math.Round(Expression.Evaluate(written, _variables).AsNumber())
            : fallback;

    void IRunContext.MarkGamepadUsed() => GamepadUsed = true;

    /// <summary>Whether the family under test told the run a controller was driven.</summary>
    public bool GamepadUsed { get; private set; }

    string IRunContext.Text(IReadOnlyDictionary<string, ExpressionNode> row, string column)
        => row.TryGetValue(column, out var written)
            ? Expression.Evaluate(written, _variables).AsText()
            : string.Empty;

    string IRunContext.Word(IReadOnlyDictionary<string, ExpressionNode> row, string column)
        => row.TryGetValue(column, out var written) ? Formula.Word(written).Trim() : string.Empty;

    Value IRunContext.Cell(IReadOnlyDictionary<string, ExpressionNode> row, string column)
        => row.TryGetValue(column, out var written)
            ? Expression.Evaluate(written, _variables)
            : Value.Null;

    string IRunContext.Give(ExecutableStep step, string key, Value value)
    {
        Written[key] = value;
        return key;
    }

    void IRunContext.Store(ExecutableStep step, string key, Value value, int depth)
    {
        Written[key] = value;
        Logged.Add((LogLevel.Info, key));
    }

    void IRunContext.GiveMatch(ExecutableStep step, ScreenPoint corner, ScreenSize size,
        double score, string text, string label, int classId)
        => Written["match"] = HitRecord.Of(corner, size, score, text, label, classId);

    void IRunContext.GiveNothing(ExecutableStep step)
    {
        Written["match"] = Value.FromText(string.Empty);
        Written["every"] = Value.FromList([]);
    }

    void IRunContext.GiveEvery(ExecutableStep step, IEnumerable<Value> hits)
        => Written["every"] = Value.FromList(hits);

    void IRunContext.Log(LogLevel level, int depth, string type, string key,
        params object[] arguments) => Logged.Add((level, key));

    void IRunContext.Saw(ExecutableStep step, LookKind kind, ImageFrame frame)
        => Shown.Add((kind, frame));

    Task<Signal> IRunContext.RunBlock(IReadOnlyList<ExecutableStep> steps, int depth,
        CancellationToken token)
    {
        Ran.Add(steps);
        return Task.FromResult(Signal.Normal);
    }

    bool IRunContext.RunStepById(string id, string type, int depth, CancellationToken token)
    {
        StepsRun.Add((id, type));
        return StepRunSucceeds;
    }

    void IRunContext.BeginScript() => ScriptOutcome = Signal.Normal;

    Signal IRunContext.EndScript() => ScriptOutcome;

    /// <summary>The steps a script under test asked the run to carry out, by identity.</summary>
    public List<(string Id, string Type)> StepsRun { get; } = [];

    /// <summary>Whether a step a script runs reports that it ended well.</summary>
    public bool StepRunSucceeds { get; set; } = true;

    /// <summary>What the run answers a script step with once the script is over.</summary>
    public Signal ScriptOutcome { get; set; } = Signal.Normal;

    /// <summary>The blocks a family under test ran, in the order it ran them.</summary>
    public List<IReadOnlyList<ExecutableStep>> Ran { get; } = [];

    void IRunContext.Watching(Action body) => body();

    IInputDevice IRunContext.Input(ExecutableStep step) => Machine.Input;

    IReadOnlyList<string> IRunContext.Shortcut(ExecutableStep step) => ["Ctrl", "C"];

    ImageFrame IRunContext.Reference(ExecutableStep step)
        => throw new NotSupportedException("this family does not read reference pictures here");
}
