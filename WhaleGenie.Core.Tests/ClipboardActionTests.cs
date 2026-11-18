using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Execution;
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
        var context = new FakeRunContext();

        await Run(context, "clipboard.writeText", ("clipboardText", "你好"));

        Assert.Equal("你好", context.Machine.Clipboard.Text);
        Assert.Contains(context.Machine.Calls,
            call => call.StartsWith("clipboardWrite", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reading_text_hands_it_over_under_the_name_the_step_gave()
    {
        var context = new FakeRunContext();
        context.Machine.Clipboard.Text = "copied words";

        await Run(context, "clipboard.readText");

        Assert.Equal("copied words", context.Written["text"].AsText());
    }

    [Fact]
    public async Task Clearing_leaves_the_clipboard_empty()
    {
        var context = new FakeRunContext();
        context.Machine.Clipboard.Text = "something";

        await Run(context, "clipboard.clear");

        Assert.Equal(string.Empty, context.Machine.Clipboard.Text);
    }

    private static Task<Signal> Run(FakeRunContext context, string type,
        params (string Name, string Text)[] fields)
        => ClipboardActions.Map[type](context, Step(type, fields), 0, CancellationToken.None);

    private static ExecutableStep Step(string type, params (string Name, string Text)[] fields)
        => new()
        {
            Type = type,
            Parameters = [.. fields.Select(field => Fields.Text(field.Name, field.Text))],
        };
}
