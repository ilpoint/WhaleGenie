using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Core.Execution;

// What a family sees of the run: the helpers families share, forwarded rather than moved so the
// engine keeps one implementation of each while the families are still being lifted out.
public sealed partial class MacroRunner : IRunContext, IRunPoints, IScreenReads, IScreenShots
{
    IDeviceLayer IRunContext.Devices => _devices;

    IDictionary<string, ImageFrame> IRunContext.Images => _run.Images;

    VariableStore IRunContext.Variables => Variables;

    string IRunContext.PathOf(ExecutableStep step, string parameter) => PathOf(step, parameter);

    IRunPoints IRunContext.Points => this;

    IScreenReads IRunContext.Reads => this;

    IReadOnlyList<(ImageFrame Frame, ScreenPoint Origin)> IScreenReads.Areas(
        ExecutableStep step) => SearchAreas(step);

    (IReadOnlyList<TextSpan> Spans, IReadOnlyList<(ImageFrame Frame, ScreenPoint Origin)> Areas)
        IScreenReads.Read(ExecutableStep step, CancellationToken token) => ReadBoth(step, token);

    (List<TextSpan> Hits, int Chosen) IScreenReads.Counting(ExecutableStep step,
        IReadOnlyList<TextSpan> readings, string wanted, string mode)
        => Counting(step, readings, wanted, mode);

    Task IScreenReads.WaitForScreen(CancellationToken token) => Gate(token);

    void IScreenReads.Showing(Action body) => Watching(body);

    Task IScreenReads.Showing(Func<Task> body) => Watching(body);

    void IScreenReads.SawSearch(ExecutableStep step, LookKind kind,
        IReadOnlyList<(ImageFrame Frame, ScreenPoint Origin)> areas,
        IReadOnlyList<(ImageMatch Match, string? Label)> found, int chosen, string wanted,
        ImageFrame? needle, double? minimum, int picture, int pictures, string? note)
        => SawSearch(step, kind, areas, found, chosen, wanted, needle, minimum, picture, pictures,
            note);

    void IScreenReads.ClickAt(ExecutableStep step, ScreenPoint at) => ClickAt(step, at);

    int IScreenReads.Beat() => LookBeat();

    IScreenShots IRunContext.Shots => this;

    IReadOnlyList<(string Name, ImageFrame Frame)> IRunContext.Pictures(ExecutableStep step)
        => [.. References(step).Select(picture => (picture.Name, picture.Frame))];

    (ScreenPoint Corner, WindowInfo? Window) IScreenShots.Spot(ExecutableStep step) => Spot(step);

    ScreenShot IScreenShots.Take(int x, int y, int width, int height, WindowInfo? window)
        => Shot(x, y, width, height, window);

    PixelColor IScreenShots.At(ExecutableStep step, ScreenPoint point, WindowInfo? window)
        => PixelAt(step, point, window);

    void IScreenShots.SawPixel(ExecutableStep step, ScreenPoint point, string looking,
        WindowInfo? window) => SawPixel(step, point, looking, window);

    void IScreenShots.Show(ExecutableStep step, LookKind kind, ImageFrame frame,
        ScreenPoint origin, IReadOnlyList<LookBox> boxes, int chosen, string looking)
        => Saw(step, kind, frame, origin, boxes, chosen, looking);

    void IScreenShots.GiveTaken(ExecutableStep step) => GiveTaken(step);

    StepArguments IRunContext.Args(ExecutableStep step) => Args(step);

    ResolvedPoint IRunPoints.Point(ExecutableStep step, string xName, string yName)
        => Point(step, xName, yName);

    ResolvedPoint IRunPoints.MovePlace(ExecutableStep step) => MovePlace(step);

    ResolvedPoint IRunPoints.DragPlace(ExecutableStep step, string end,
        (ScreenPoint? Corner, WindowInfo? Window) anchor) => DragPlace(step, end, anchor);

    (ScreenPoint? Corner, WindowInfo? Window) IRunPoints.AnchorOf(ExecutableStep step)
        => AnchorOf(step);

    bool IRunPoints.PostsMessages(ExecutableStep step) => PostsMessages(step);

    ScreenPoint IRunPoints.Scatter(ExecutableStep step, ScreenPoint point)
        => Scattered(point, Spread(step));

    void IRunPoints.GivePoints(ExecutableStep step, ScreenPoint screen, ScreenPoint? window,
        string screenKey, string windowKey)
        => GivePoints(step, screen, window, screenKey, windowKey);

    void IRunPoints.GivePointAt(ExecutableStep step, ScreenPoint screen, WindowInfo? window)
        => GivePointAt(step, screen, window);

    bool IRunContext.Flag(ExecutableStep step, string name, bool fallback)
        => Flag(step, name, fallback);

    bool IRunContext.CaseMatches(ExecutableStep step, Value value, string wanted, string mode)
        => CaseMatches(step, value, wanted, mode);

    bool IRunContext.Wants(ExecutableStep step, string key) => Wants(step, key);

    WindowRule IRunContext.WindowRuleOf(ExecutableStep step) => WindowRuleOf(step);

    WindowInfo? IRunContext.Lookup(ExecutableStep step) => Lookup(step);

    WindowInfo IRunContext.Locate(ExecutableStep step) => Locate(step);

    (int Left, int Top, int Width, int Height)? IRunContext.RectOf(ExecutableStep step, string name)
        => RecordedRect(step, name);

    int IRunContext.Index(ExecutableStep step, string name, int fallback)
        => Index(step, name, fallback);

    string IRunContext.Button(ExecutableStep step) => Button(step);

    IReadOnlyList<string> IRunContext.Keys(ExecutableStep step) => Keys(step);

    int IRunContext.Pace(int milliseconds) => Pace(milliseconds);

    int IRunContext.Number(IReadOnlyDictionary<string, ExpressionNode> row, string column,
        int fallback) => Cell(row, column, fallback);

    void IRunContext.MarkGamepadUsed() => _run.GamepadUsed = true;

    string IRunContext.Text(IReadOnlyDictionary<string, ExpressionNode> row, string column)
        => Text(row, column);

    string IRunContext.Word(IReadOnlyDictionary<string, ExpressionNode> row, string column)
        => Word(row, column);

    Value IRunContext.Cell(IReadOnlyDictionary<string, ExpressionNode> row, string column)
        => CellValue(row, column);

    string IRunContext.Give(ExecutableStep step, string key, Value value)
        => Give(step, key, value);

    void IRunContext.Store(ExecutableStep step, string key, Value value, int depth)
        => Store(step, key, value, depth);

    void IRunContext.GiveMatch(ExecutableStep step, ScreenPoint corner, ScreenSize size,
        double score, string text, string label, int classId)
        => GiveMatch(step, corner, size, score, text, label, classId);

    void IRunContext.GiveNothing(ExecutableStep step) => GiveNothing(step);

    void IRunContext.GiveEvery(ExecutableStep step, IEnumerable<Value> hits)
        => GiveEvery(step, hits);

    void IRunContext.Log(LogLevel level, int depth, string type, string key,
        params object[] arguments)
        => Log(level, depth, type, key, arguments);

    Task<Signal> IRunContext.RunBlock(IReadOnlyList<ExecutableStep> steps, int depth,
        CancellationToken token) => RunSteps(steps, depth, token);

    bool IRunContext.RunStepById(string id, string type, int depth, CancellationToken token)
        => RunStepById(id, type, depth, token);

    void IRunContext.BeginScript()
    {
        _run.StoppedInsideScript = false;
        _run.ReturnedInsideScript = false;
    }

    Signal IRunContext.EndScript()
        => _run.StoppedInsideScript ? Signal.Stop
            : _run.ReturnedInsideScript ? Signal.Return
            : Signal.Normal;

    void IRunContext.Saw(ExecutableStep step, LookKind kind, ImageFrame frame)
        => Saw(step, kind, frame, default, [], 0);

    void IRunContext.Watching(Action body) => Watching(body);

    IInputDevice IRunContext.Input(ExecutableStep step) => Input(step);

    IReadOnlyList<string> IRunContext.Shortcut(ExecutableStep step) => Shortcut(step);

    ImageFrame IRunContext.Reference(ExecutableStep step) => Reference(step);
}
