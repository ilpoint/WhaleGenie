using System;
using System.Collections.Generic;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Core.Execution;

// What a family sees of the run: the helpers families share, forwarded rather than moved so the
// engine keeps one implementation of each while the families are still being lifted out.
public sealed partial class MacroRunner : IRunContext
{
    IDeviceLayer IRunContext.Devices => _devices;

    IDictionary<string, ImageFrame> IRunContext.Images => _run.Images;

    VariableStore IRunContext.Variables => Variables;

    string IRunContext.PathOf(ExecutableStep step, string parameter) => PathOf(step, parameter);

    StepArguments IRunContext.Args(ExecutableStep step) => Args(step);

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

    void IRunContext.Saw(ExecutableStep step, LookKind kind, ImageFrame frame)
        => Saw(step, kind, frame, default, [], 0);

    void IRunContext.Watching(Action body) => Watching(body);

    IInputDevice IRunContext.Input(ExecutableStep step) => Input(step);

    IReadOnlyList<string> IRunContext.Shortcut(ExecutableStep step) => Shortcut(step);

    ImageFrame IRunContext.Reference(ExecutableStep step) => Reference(step);
}
