using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Execution;
using WhaleGenie.TestSupport;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The pointer family on its own: the fake input device writes down where the pointer was sent, so a
/// move, a click or a drag can be checked without the real cursor leaving the corner of the screen.
/// </summary>
public class PointerActionTests
{
    [Fact]
    public async Task A_move_puts_the_pointer_where_the_step_says()
    {
        var context = new FakeRunContext();

        await Run(context, "input.mouseMove", Step("input.mouseMove",
            Fields.Text("x", "10"),
            Fields.Text("y", "20")));

        Assert.Contains("move 10 20 0", context.Machine.Desk.Calls);
    }

    [Fact]
    public async Task A_click_is_sent_with_its_button_and_repeat()
    {
        var context = new FakeRunContext();

        await Run(context, "input.mouseClick", Step("input.mouseClick",
            Fields.Text("x", "10"),
            Fields.Text("y", "20"),
            Fields.Text("button", "right"),
            Fields.Text("repeat", "2")));

        Assert.Contains("click right 10 20 2 0", context.Machine.Desk.Calls);
    }

    [Fact]
    public async Task A_drag_presses_at_one_end_and_lets_go_at_the_other()
    {
        var context = new FakeRunContext();

        await Run(context, "input.mouseDrag", Step("input.mouseDrag",
            Fields.Text("startX", "1"),
            Fields.Text("startY", "2"),
            Fields.Text("endX", "3"),
            Fields.Text("endY", "4")));

        Assert.Contains("drag left 1 2 3 4 0 20", context.Machine.Desk.Calls);
    }

    [Fact]
    public async Task A_scroll_turns_the_wheel_by_whole_notches()
    {
        var context = new FakeRunContext();

        await Run(context, "input.mouseScroll", Step("input.mouseScroll",
            Fields.Text("direction", "up"),
            Fields.Text("amount", "2")));

        Assert.Contains("scroll up 240 0 0", context.Machine.Desk.Calls);
    }

    private static Task<Signal> Run(FakeRunContext context, string type, ExecutableStep step)
        => PointerActions.Map[type](context, step, 0, CancellationToken.None);

    private static ExecutableStep Step(string type, params ExecutableParameter[] parameters)
        => new() { Type = type, Parameters = parameters };
}
