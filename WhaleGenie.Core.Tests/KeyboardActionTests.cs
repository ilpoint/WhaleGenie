using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Execution;
using WhaleGenie.TestSupport;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The keyboard family on its own: the fake input device writes down what it was asked to press, so
/// a key press can be checked without a keystroke reaching this computer.
/// </summary>
public class KeyboardActionTests
{
    [Fact]
    public async Task A_key_is_pressed_with_the_hold_the_step_asked_for()
    {
        var context = new FakeRunContext();

        await Run(context, "input.keyPress", Step("input.keyPress",
            Fields.Text("key", "F5"),
            Fields.Text("holdMs", "40")));

        Assert.Contains("keyPress F5 40", context.Machine.Desk.Calls);
    }

    [Fact]
    public async Task A_hotkey_sends_every_key_of_the_combination()
    {
        var context = new FakeRunContext();

        await Run(context, "input.hotkey", Step("input.hotkey",
            Fields.Text("keys", "Ctrl+Shift+S")));

        Assert.Contains("hotkey Ctrl|Shift|S 0", context.Machine.Desk.Calls);
    }

    [Fact]
    public async Task Typing_hands_the_text_and_the_beat_to_the_device()
    {
        var context = new FakeRunContext();

        await Run(context, "input.typeText", Step("input.typeText",
            Fields.Text("typedText", "hello"),
            Fields.Text("intervalMs", "15")));

        Assert.Contains("type hello 15", context.Machine.Desk.Calls);
    }

    private static Task<Signal> Run(FakeRunContext context, string type, ExecutableStep step)
        => KeyboardActions.Map[type](context, step, 0, CancellationToken.None);

    private static ExecutableStep Step(string type, params ExecutableParameter[] parameters)
        => new() { Type = type, Parameters = parameters };
}
