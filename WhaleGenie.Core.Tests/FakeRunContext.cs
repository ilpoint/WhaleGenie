using System;
using System.Collections.Generic;
using System.Globalization;
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

    VariableStore IRunContext.Variables => _variables;

    string IRunContext.PathOf(ExecutableStep step, string parameter)
        => ((IRunContext)this).Args(step).ValueOf(parameter).AsText();

    StepArguments IRunContext.Args(ExecutableStep step) => new(step, _variables, 16);

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

    string IRunContext.Button(ExecutableStep step)
    {
        var button = step.Text("button").Trim();
        return button.Length == 0 ? "left" : button;
    }

    string IRunContext.Text(IReadOnlyDictionary<string, ExpressionNode> row, string column)
        => row.TryGetValue(column, out var written)
            ? Expression.Evaluate(written, _variables).AsText()
            : string.Empty;

    string IRunContext.Word(IReadOnlyDictionary<string, ExpressionNode> row, string column)
        => row.TryGetValue(column, out var written) ? Formula.Word(written).Trim() : string.Empty;

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

    void IRunContext.Watching(Action body) => body();

    IInputDevice IRunContext.Input(ExecutableStep step) => Machine.Input;

    IReadOnlyList<string> IRunContext.Shortcut(ExecutableStep step) => ["Ctrl", "C"];

    ImageFrame IRunContext.Reference(ExecutableStep step)
        => throw new NotSupportedException("this family does not read reference pictures here");
}
