using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Recording;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The actions that drive the machine. They run against a stand-in for the device layer, so
/// the checks are about what the engine asked for, not about what happened to the desktop.
/// </summary>
[Collection("InputGate")]
public partial class DeviceActionTests
{
    /// <summary>
    /// A step as the file now writes it. Window rules are one record, so the checks may keep saying
    /// a title and the one choice beside it while the engine is handed the shape a saved macro holds.
    /// </summary>
    private static ExecutableStep Step(string type, params ExecutableParameter[] parameters)
    {
        var hasWindow = parameters.Any(parameter => parameter.Name == "window");
        var how = hasWindow
            ? parameters.FirstOrDefault(parameter => parameter.Name == "how") is { } asked
                ? Formula.Word(asked.Written)
                : null
            : null;
        var written = new List<ExecutableParameter>(parameters.Length);
        foreach (var parameter in parameters)
        {
            if (hasWindow && parameter.Name == "how")
            {
                continue;
            }

            if (parameter.Name != "window")
            {
                written.Add(parameter);
                continue;
            }

            if (parameter.Written is VariableNode)
            {
                written.Add(parameter);
                continue;
            }

            var text = Formula.Word(parameter.Written);

            var rule = WindowRule.TryParse(text, out var parsed)
                ? parsed
                : new WindowRule(Title: text);
            if (how is { Length: > 0 })
            {
                rule = how == WindowHow.Handle
                    ? rule with { How = how, Handle = long.TryParse(text, out var handle) ? handle : 0 }
                    : how is WindowHow.Process or WindowHow.ProcessEquals
                    ? rule with { How = how, Process = text }
                    : how is WindowHow.Class or WindowHow.ClassEquals
                        ? rule with { How = how, ClassName = text }
                        : rule with { How = how, Title = text };
            }

            written.Add(Param("window", rule.ToJson().ToJsonString()));
        }

        return new ExecutableStep { Type = type, Parameters = [.. written] };
    }

    /// <summary>See <see cref="SampleFiles.Within"/>: the same step, allowed this long.</summary>
    private static ExecutableStep Within(int milliseconds, ExecutableStep step,
        StepErrorAction onError = StepErrorAction.Stop, string id = "")
        => SampleFiles.Within(milliseconds, step, onError, id);

    private static ExecutableParameter Param(string name, string text = "")
        => Fields.Text(name, text);

    /// <summary>One field whose value is already a shape rather than a line.</summary>
    private static ExecutableParameter Param(string name, ExpressionNode value)
    => Fields.Computed(name, value);

    /// <summary>窗口格写成「沿用上一次用的窗口」时，文件里那一小段标记。</summary>
    private static string Following => WindowRule.FollowPrevious.ToJson().ToJsonString();

    /// <summary>See <see cref="SampleFiles.Returns"/>: the results a step hands over.</summary>
    private static ExecutableParameter Returns(params string[] results)
        => SampleFiles.Returns(results);

    /// <summary>See <see cref="SampleFiles.Hit"/>: a place, a picture or a program and all its parts.</summary>
    private static ExecutableParameter Hit(string key, string name, bool every = false)
        => SampleFiles.Hit(key, name, every);

    /// <summary>See <see cref="SampleFiles.Parts"/>: a measured value beside every part of it.</summary>
    private static ExecutableParameter Parts(string key, string name, params string[] extra)
        => SampleFiles.Parts(key, name, extra);

    /// <summary>One field of a record a step handed over, read the way an expression reads it.</summary>
    private static Value Field(VariableStore store, string name, string field)
        => store.Local.Values[name].Field(field)
            ?? throw new InvalidOperationException($"\"{name}\" has no field \"{field}\"");

    /// <summary>The middle of a hit, written "x,y" the way a position was written before records.</summary>
    private static string Centre(VariableStore store, string name)
        => $"{Field(store, name, "x").AsText()},{Field(store, name, "y").AsText()}";

    /// <summary>The middle a record says about itself, the same way, for one already in hand.</summary>
    private static string MiddleOf(Value record)
        => $"{record.Field("x")!.AsText()},{record.Field("y")!.AsText()}";

    /// <summary>Where a place starts, written "left,top" the way a search region counts from.</summary>
    private static string Corner(VariableStore store, string name)
        => $"{Field(store, name, "left").AsText()},{Field(store, name, "top").AsText()}";

    /// <summary>The middle of every hit in one "every match" list, in the order they are counted in.</summary>
    private static string Centres(VariableStore store, string name)
        => string.Join(", ", store.Local.Values[name + ".every"].AsList()
            .Select(hit => $"{hit.Field("x")!.AsText()},{hit.Field("y")!.AsText()}"));

    /// <summary>The title of every window in one listing, in the order the system listed them.</summary>
    private static IEnumerable<string> Titles(VariableStore store, string name)
        => store.Local.Values[name].AsList().Select(window => window.Field("title")!.AsText());

    private static ExecutableParameter Body(string name, params ExecutableStep[] steps)
        => Fields.Steps(name, steps);

    /// <summary>See <see cref="SampleFiles.Region"/>: the places a step looks at, one row per place.</summary>
    private static ExecutableParameter Region(params (int X, int Y, int Width, int Height)[] rectangles)
        => SampleFiles.Region(rectangles);

    /// <summary>See <see cref="SampleFiles.Points"/>: the colours a pattern wants around its base.</summary>
    private static ExecutableParameter Points(params (string Dx, string Dy, string Colour)[] offsets)
        => SampleFiles.Points(offsets);

    /// <summary>The same, for a colour that says how far off it may be on its own.</summary>
    private static ExecutableParameter PointsWithTolerance(
        params (string Dx, string Dy, string Colour, string Tolerance)[] offsets)
        => SampleFiles.PointsWithTolerance(offsets);

    /// <summary>See <see cref="SampleFiles.RegionFrom"/>: a place that names a variable.</summary>
    private static ExecutableParameter RegionFrom(string named) => SampleFiles.RegionFrom(named);

    private static ExecutableParameter RegionFrom(ExpressionNode written)
        => SampleFiles.RegionFrom(written);

    /// <summary>See <see cref="SampleFiles.Capture"/>: a step that copies one part of the screen.</summary>
    private static ExecutableStep Capture(int x, int y, int width, int height,
        params ExecutableParameter[] rest) => SampleFiles.Capture(x, y, width, height, rest);

    /// <summary>See <see cref="SampleFiles.Recognize"/>: a step that reads one part of the screen.</summary>
    private static ExecutableStep Recognize(int x, int y, int width, int height,
        params ExecutableParameter[] rest) => SampleFiles.Recognize(x, y, width, height, rest);

    /// <summary>See <see cref="SampleFiles.CaptureWholeScreen"/>: a step that names no rectangle.</summary>
    private static ExecutableStep CaptureWholeScreen(params ExecutableParameter[] rest)
        => SampleFiles.CaptureWholeScreen(rest);

    /// <summary>See <see cref="SampleFiles.RecognizeWholeScreen"/>: a step that names no rectangle.</summary>
    private static ExecutableStep RecognizeWholeScreen(params ExecutableParameter[] rest)
        => SampleFiles.RecognizeWholeScreen(rest);

    /// <summary>A parameter that holds a list of rows: the places to look, the presses of a run.</summary>
    private static ExecutableParameter Rows(string name,
        params IReadOnlyDictionary<string, ExpressionNode>[] rows)
        => Fields.Rows(name, rows);

    /// <summary>One row of what a browser step narrows the page down by, as the dialog writes it.</summary>
    private static IReadOnlyDictionary<string, ExpressionNode> Narrow(string how, string value = "")
        => Cells.Of(("how", how), ("value", value));

    /// <summary>
    /// The pictures a step looks for, written the way the dialog writes them: one row each, tried
    /// in the order they are listed.
    /// </summary>
    private static ExecutableParameter Pictures(params string[] written)
        => Pictures([.. written.Select(text => (ExpressionNode)Cells.Line(text))]);

    private static ExecutableParameter Pictures(params ExpressionNode[] written)
        => new()
        {
            Name = "images",
            Rows = [.. written.Select(value =>
                new Dictionary<string, ExpressionNode>(StringComparer.OrdinalIgnoreCase)
                {
                    ["image"] = value,
                })],
        };

    /// <summary>One press of a key run, with its own hold or gap where it has one.</summary>
    private static IReadOnlyDictionary<string, ExpressionNode> Press(string keys, int? holdMs = null,
        int? gapMs = null)
    {
        var row = new Dictionary<string, ExpressionNode>(StringComparer.OrdinalIgnoreCase)
        {
            ["keys"] = Cells.Line(keys),
        };

        if (holdMs is { } hold)
        {
            row["holdMs"] = Cells.Line(hold.ToString(CultureInfo.InvariantCulture));
        }

        if (gapMs is { } gap)
        {
            row["gapMs"] = Cells.Line(gap.ToString(CultureInfo.InvariantCulture));
        }

        return row;
    }

    private static ExecutableParameter When(string name, ExecutableStep step)
        => Fields.Run(name, TestConditions.Expression(step));

    private static async Task<(RunResult Result, FakeDeviceLayer Devices, VariableStore Store)> RunAsync(
        ExecutableStep[] steps, FakeDeviceLayer? devices = null)
    {
        var layer = devices ?? new FakeDeviceLayer();
        var store = new VariableStore();
        var result = await new MacroRunner(store, new AgreeingHost(), layer).RunAsync(steps);
        return (result, layer, store);
    }

    /// <summary>
    /// A run with nobody watching that still agrees to what cannot be undone. What these cases are
    /// about is the request a device was given, not whether somebody would have said yes to it;
    /// the asking itself is the subject of <c>IrreversibleStepTests</c>.
    /// </summary>
    private sealed class AgreeingHost : IRunHost
    {
        public void Log(LogEntry entry)
        {
        }

        public string Text(LogEntry entry) => entry.Key;

        public Task<bool> BeforeStep(ExecutableStep step, int depth, CancellationToken token)
            => Task.FromResult(true);

        public Task<StepErrorChoice> Ask(string step, string reason, string detail, string comment,
            CancellationToken token)
            => Task.FromResult(StepErrorChoice.Stop);

        public Task<AskAnswer> AskUser(AskQuestion question, CancellationToken token)
            => Task.FromResult(AskAnswer.Cancel);

        public Task<bool> ConfirmIrreversible(ExecutableStep step, CancellationToken token)
            => Task.FromResult(true);
    }

    /// <summary>The same, with a project's macros behind the run so a step can call them.</summary>
    private static async Task<(RunResult Result, FakeDeviceLayer Devices, VariableStore Store)> RunWithAsync(
        ExecutableStep[] steps, FakeDeviceLayer devices, IMacroLibrary macros)
    {
        var store = new VariableStore();
        var result = await new MacroRunner(store, new SilentRunHost(), devices, 1, macros)
            .RunAsync(steps);
        return (result, devices, store);
    }

    /// <summary>
    /// What a run asked the devices for, without the text file a failed run leaves behind. These
    /// cases are each about one request, and that file is about the run's own ending rather than
    /// about the request, so leaving it out keeps them about what they are about.
    /// </summary>
    private static string[] Asked(FakeDeviceLayer devices)
        => [.. devices.Calls.Where(call => !call.Contains(@"logs\failure-", StringComparison.Ordinal))];
}


/// <summary>
/// A device layer that writes down what it was asked to do instead of doing it, so the
/// engine can be checked without touching the real keyboard, mouse or screen.
/// </summary>
/// <summary>The hook a test can press keys on, so a wait can be ended from the test rather than by hand.</summary>
internal sealed class TestInputHook : IInputHook
{
    public event Action<KeyHookEvent>? KeyDown;

    public event Action<KeyHookEvent>? KeyUp;

    public event Action<MouseHookEvent>? MouseDown { add { } remove { } }

    public event Action<MouseHookEvent>? MouseUp { add { } remove { } }

    public event Action<MouseMovedHookEvent>? Moved { add { } remove { } }

    public event Action<MouseMovedRelativeHookEvent>? MovedRelative { add { } remove { } }

    public event Action<WheelHookEvent>? Wheel { add { } remove { } }

    public bool HasKeyDown => KeyDown is not null;

    public KeyHookEvent? Last { get; private set; }

    public void Press(string name, bool up = false)
    {
        var key = KeyNames.Resolve(name) ?? throw new ArgumentException(name, nameof(name));
        var seen = new KeyHookEvent(key, injected: false);
        Last = seen;
        if (up)
        {
            KeyUp?.Invoke(seen);
        }
        else
        {
            KeyDown?.Invoke(seen);
        }
    }
}
