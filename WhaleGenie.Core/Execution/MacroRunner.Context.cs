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
        ExecutableStep step) => _sight.SearchAreas(step);

    (IReadOnlyList<TextSpan> Spans, IReadOnlyList<(ImageFrame Frame, ScreenPoint Origin)> Areas)
        IScreenReads.Read(ExecutableStep step, CancellationToken token) => _reads.ReadBoth(step, token);

    (List<TextSpan> Hits, int Chosen) IScreenReads.Counting(ExecutableStep step,
        IReadOnlyList<TextSpan> readings, string wanted, string mode)
        => _reads.Counting(step, readings, wanted, mode);

    Task IScreenReads.WaitForScreen(CancellationToken token) => _sight.Gate(token);

    void IScreenReads.Showing(Action body) => _sight.Watching(body);

    Task IScreenReads.Showing(Func<Task> body) => _sight.Watching(body);

    void IScreenReads.SawSearch(ExecutableStep step, LookKind kind,
        IReadOnlyList<(ImageFrame Frame, ScreenPoint Origin)> areas,
        IReadOnlyList<(ImageMatch Match, string? Label)> found, int chosen, string wanted,
        ImageFrame? needle, double? minimum, int picture, int pictures, string? note)
        => _sight.SawSearch(step, kind, areas, found, chosen, wanted, needle, minimum, picture, pictures,
            note);

    void IScreenReads.ClickAt(ExecutableStep step, ScreenPoint at) => ClickAt(step, at);

    IScreenShots IRunContext.Shots => this;

    IReadOnlyList<(string Name, ImageFrame Frame)> IRunContext.Pictures(ExecutableStep step)
        => [.. _pictures.All(step).Select(picture => (picture.Name, picture.Frame))];

    (ScreenPoint Corner, WindowInfo? Window) IScreenShots.Spot(ExecutableStep step) => _points.Spot(step);

    ScreenShot IScreenShots.Take(int x, int y, int width, int height, WindowInfo? window)
        => _sight.Shot(x, y, width, height, window);

    PixelColor IScreenShots.At(ExecutableStep step, ScreenPoint point, WindowInfo? window)
        => _sight.PixelAt(step, point, window);

    void IScreenShots.SawPixel(ExecutableStep step, ScreenPoint point, string looking,
        WindowInfo? window) => _sight.SawPixel(step, point, looking, window);

    void IScreenShots.Show(ExecutableStep step, LookKind kind, ImageFrame frame,
        ScreenPoint origin, IReadOnlyList<LookBox> boxes, int chosen, string looking)
        => _sight.Saw(step, kind, frame, origin, boxes, chosen, looking);

    void IScreenShots.GiveTaken(ExecutableStep step) => _results.GiveTaken(step);

    StepArguments IRunContext.Args(ExecutableStep step) => Args(step);

    ResolvedPoint IRunPoints.Point(ExecutableStep step, string xName, string yName)
        => _points.Point(step, xName, yName);

    ResolvedPoint IRunPoints.MovePlace(ExecutableStep step) => _points.MovePlace(step);

    ResolvedPoint IRunPoints.DragPlace(ExecutableStep step, string end,
        (ScreenPoint? Corner, WindowInfo? Window) anchor) => _points.DragPlace(step, end, anchor);

    (ScreenPoint? Corner, WindowInfo? Window) IRunPoints.AnchorOf(ExecutableStep step)
        => _windows.AnchorOf(step);

    bool IRunPoints.PostsMessages(ExecutableStep step) => ScreenPoints.PostsMessages(step);

    ScreenPoint IRunPoints.Scatter(ExecutableStep step, ScreenPoint point)
        => _points.Scatter(step, point);

    void IRunPoints.GivePoints(ExecutableStep step, ScreenPoint screen, ScreenPoint? window,
        string screenKey, string windowKey)
        => _results.GivePoints(step, screen, window, screenKey, windowKey);

    void IRunPoints.GivePointAt(ExecutableStep step, ScreenPoint screen, WindowInfo? window)
        => _results.GivePointAt(step, screen, window);

    bool IRunContext.Flag(ExecutableStep step, string name, bool fallback)
        => Flag(step, name, fallback);

    bool IRunContext.CaseMatches(ExecutableStep step, Value value, string wanted, string mode)
        => CaseMatches(step, value, wanted, mode);

    bool IRunContext.Wants(ExecutableStep step, string key) => StepResults.Wants(step, key);

    WindowRule IRunContext.WindowRuleOf(ExecutableStep step) => _windows.RuleOf(step);

    WindowInfo? IRunContext.Lookup(ExecutableStep step) => _windows.Lookup(step);

    WindowInfo IRunContext.Locate(ExecutableStep step) => _windows.Locate(step);

    (int Left, int Top, int Width, int Height)? IRunContext.RectOf(ExecutableStep step, string name)
        => RecordedRect(step, name);

    int IRunContext.Index(ExecutableStep step, string name, int fallback)
        => Index(step, name, fallback);

    string IRunContext.Button(ExecutableStep step) => ScreenPoints.Button(step);

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
        => _results.Give(step, key, value);

    void IRunContext.Store(ExecutableStep step, string key, Value value, int depth)
        => _results.Store(step, key, value, depth);

    void IRunContext.GiveMatch(ExecutableStep step, ScreenPoint corner, ScreenSize size,
        double score, string text, string label, int classId)
        => _results.GiveMatch(step, corner, size, score, text, label, classId);

    void IRunContext.GiveNothing(ExecutableStep step) => _results.GiveNothing(step);

    void IRunContext.GiveEvery(ExecutableStep step, IEnumerable<Value> hits)
        => _results.GiveEvery(step, hits);

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
        => _sight.Saw(step, kind, frame, default, [], 0);

    void IRunContext.Watching(Action body) => _sight.Watching(body);

    IInputDevice IRunContext.Input(ExecutableStep step) => _points.Input(step);

    IReadOnlyList<string> IRunContext.Shortcut(ExecutableStep step) => Shortcut(step);

    ImageFrame IRunContext.Reference(ExecutableStep step) => _pictures.One(step);
}
