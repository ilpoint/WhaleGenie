using System;
using System.Collections.Generic;
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
internal sealed class FakeRunContext : IRunContext
{
    private readonly VariableStore _variables = new();

    public FakeDeviceLayer Machine { get; } = new();

    public Dictionary<string, ImageFrame> Images { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, Value> Written { get; } = new(StringComparer.Ordinal);

    public List<(LogLevel Level, string Key)> Logged { get; } = [];

    public List<(LookKind Kind, ImageFrame Frame)> Shown { get; } = [];

    IDeviceLayer IRunContext.Devices => Machine;

    IDictionary<string, ImageFrame> IRunContext.Images => Images;

    StepArguments IRunContext.Args(ExecutableStep step) => new(step, _variables, 16);

    bool IRunContext.Flag(ExecutableStep step, string name, bool fallback)
    {
        var text = step.Text(name).Trim();
        return text.Length == 0
            ? fallback
            : !string.Equals(text, "false", StringComparison.OrdinalIgnoreCase);
    }

    bool IRunContext.Wants(ExecutableStep step, string key) => step.Return(key).Length > 0;

    WindowRule IRunContext.WindowRuleOf(ExecutableStep step)
        => WindowRule.TryParse(step.Text("window"), out var rule) ? rule : WindowRule.Empty;

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

    void IRunContext.Log(LogLevel level, int depth, string type, string key,
        params object[] arguments) => Logged.Add((level, key));

    void IRunContext.Saw(ExecutableStep step, LookKind kind, ImageFrame frame)
        => Shown.Add((kind, frame));

    void IRunContext.Watching(Action body) => body();

    IInputDevice IRunContext.Input(ExecutableStep step) => Machine.Input;

    IReadOnlyList<string> IRunContext.Shortcut(ExecutableStep step) => ["Ctrl", "C"];

    ImageFrame IRunContext.Reference(ExecutableStep step)
        => throw new NotSupportedException("this family does not read reference pictures here");
}
