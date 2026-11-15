using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.TestSupport;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The clipboard family on its own: a step, a context that records what it was asked to do, and no
/// runner. This is what the family split is for — an action can be tried without the engine around
/// it, and a change to one family no longer needs the whole run to compile against.
/// </summary>
public class ClipboardActionTests
{
    [Fact]
    public async Task Writing_text_puts_it_on_the_clipboard()
    {
        var context = new Context();

        await Run(context, "clipboard.writeText", ("clipboardText", "你好"));

        Assert.Equal("你好", context.Machine.Clipboard.Text);
        Assert.Contains(context.Machine.Calls,
            call => call.StartsWith("clipboardWrite", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reading_text_hands_it_over_under_the_name_the_step_gave()
    {
        var context = new Context();
        context.Machine.Clipboard.Text = "copied words";

        await Run(context, "clipboard.readText");

        Assert.Equal("copied words", context.Written["text"].AsText());
    }

    [Fact]
    public async Task Clearing_leaves_the_clipboard_empty()
    {
        var context = new Context();
        context.Machine.Clipboard.Text = "something";

        await Run(context, "clipboard.clear");

        Assert.Equal(string.Empty, context.Machine.Clipboard.Text);
    }

    private static Task<Signal> Run(Context context, string type,
        params (string Name, string Text)[] fields)
        => ClipboardActions.Map[type](context, Step(type, fields), 0, CancellationToken.None);

    private static ExecutableStep Step(string type, params (string Name, string Text)[] fields)
        => new()
        {
            Type = type,
            Parameters = [.. fields.Select(field => Fields.Text(field.Name, field.Text))],
        };

    /// <summary>
    /// The run as one family sees it: the devices, the results, the log and the picture it showed,
    /// recorded rather than performed.
    /// </summary>
    private sealed class Context : IRunContext
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
}
