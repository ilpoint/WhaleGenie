using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Execution;
using WhaleGenie.TestSupport;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The controller family on its own: the fake pad writes down every press, and the run is told a
/// controller was used so the button is let go of when the run ends.
/// </summary>
public class GamepadActionTests
{
    [Fact]
    public async Task Connecting_a_controller_tells_the_run_it_was_used()
    {
        var context = new FakeRunContext();

        await Run(context, "gamepad.connect", Step("gamepad.connect"));

        Assert.Contains("gamepadConnect", context.Machine.Desk.Calls);
        Assert.True(context.GamepadUsed);
    }

    [Fact]
    public async Task Tapping_a_button_presses_it_and_lets_it_go_again()
    {
        var context = new FakeRunContext();

        await Run(context, "gamepad.button", Step("gamepad.button",
            Fields.Text("button", "A")));

        var presses = context.Machine.Desk.Calls
            .Where(call => call.StartsWith("gamepadButton A", System.StringComparison.Ordinal))
            .ToList();
        Assert.Equal(["gamepadButton A True", "gamepadButton A False"], presses);
        Assert.True(context.GamepadUsed);
    }

    private static Task<Signal> Run(FakeRunContext context, string type, ExecutableStep step)
        => GamepadActions.Map[type](context, step, 0, CancellationToken.None);

    private static ExecutableStep Step(string type, params ExecutableParameter[] parameters)
        => new() { Type = type, Parameters = parameters };
}
