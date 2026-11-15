using System;
using System.Collections.Generic;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Core.Execution;

// What a family sees of the run: the helpers families share, forwarded rather than moved so the
// engine keeps one implementation of each while the families are still being lifted out.
public sealed partial class MacroRunner : IRunContext
{
    IDeviceLayer IRunContext.Devices => _devices;

    IDictionary<string, ImageFrame> IRunContext.Images => _run.Images;

    StepArguments IRunContext.Args(ExecutableStep step) => Args(step);

    bool IRunContext.Flag(ExecutableStep step, string name, bool fallback)
        => Flag(step, name, fallback);

    bool IRunContext.Wants(ExecutableStep step, string key) => Wants(step, key);

    WindowRule IRunContext.WindowRuleOf(ExecutableStep step) => WindowRuleOf(step);

    string IRunContext.Give(ExecutableStep step, string key, Value value)
        => Give(step, key, value);

    void IRunContext.Store(ExecutableStep step, string key, Value value, int depth)
        => Store(step, key, value, depth);

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
