using System.Linq;

using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The failure a step reports when its own clock runs out. A step that was waiting for something
/// names what it waited for rather than only that it was slow, so the user knows which picture, text
/// or program never turned up.
/// </summary>
public class StepTimeoutTests
{
    private const int Patience = 1250;

    private static (StepTimeout Timeout, FakeRunContext Run) Subject()
    {
        var run = new FakeRunContext();
        var windows = new WindowFacts(run.Machine, new VariableStore(), new RunState(),
            EvaluationContext.System);
        return (new StepTimeout(run, windows), run);
    }

    private static ExecutableStep Step(string type, params ExecutableParameter[] parameters) => new()
    {
        Type = type,
        Parameters = [.. parameters],
        Meta = new StepMeta { TimeoutMs = Patience },
    };

    [Fact]
    public void A_step_with_nothing_to_name_only_says_it_was_slow()
    {
        var (timeout, _) = Subject();

        var failure = timeout.For(Step("nope.unknown"));

        Assert.Equal(RunKeys.Timeout, failure.Key);
        Assert.Equal("1250", failure.Detail);
    }

    [Fact]
    public void A_step_waiting_for_a_colour_names_the_colour()
    {
        var (timeout, _) = Subject();

        Assert.Equal(RunKeys.WaitColorTimeout,
            timeout.For(Step("vision.waitColor", Fields.Text("color", "#00FF00"))).Key);
        Assert.Equal("#00FF00",
            timeout.For(Step("vision.waitColor", Fields.Text("color", "#00FF00"))).Detail);

        var click = timeout.For(Step("vision.clickColor", Fields.Text("color", "#00FF00")));
        Assert.Equal(RunKeys.ColourNotFound, click.Key);
        Assert.Equal("#00FF00", click.Detail);
    }

    [Fact]
    public void A_step_waiting_for_text_names_the_text()
    {
        var (timeout, _) = Subject();

        var failure = timeout.For(Step("ocr.clickText", Fields.Text("queryText", "Paused")));

        Assert.Equal(RunKeys.TextNotFound, failure.Key);
        Assert.Equal("Paused", failure.Detail);
    }

    [Fact]
    public void A_step_waiting_for_an_element_names_the_selector()
    {
        var (timeout, _) = Subject();

        foreach (var type in new[] { "uia.waitElement", "uia.click", "uia.invoke" })
        {
            var failure = timeout.For(Step(type, Fields.Text("uiSelector", "name=OK")));
            Assert.Equal(RunKeys.ElementNotFound, failure.Key);
            Assert.Equal("name=OK", failure.Detail);
        }
    }

    [Fact]
    public void A_step_waiting_for_a_command_names_the_command_file()
    {
        var (timeout, _) = Subject();

        var failure = timeout.For(Step("command.run", Fields.Text("commandFile", "run.bat")));

        Assert.Equal(RunKeys.CommandTimeout, failure.Key);
        Assert.Equal("run.bat", failure.Detail);
    }

    [Theory]
    [InlineData("clipboard.waitChange", RunKeys.ClipboardTimeout)]
    [InlineData("clipboard.copy", RunKeys.ClipboardTimeout)]
    [InlineData("vision.waitStable", RunKeys.ScreenNotStable)]
    public void A_step_waiting_with_nothing_to_name_says_what_it_waited_for(string type, string key)
        => Assert.Equal(key, Subject().Timeout.For(Step(type)).Key);

    [Theory]
    [InlineData("process.waitFor", RunKeys.ProgramNotFound)]
    [InlineData("process.waitExit", RunKeys.ProcessTimeout)]
    [InlineData("window.waitFor", RunKeys.WindowTimeout)]
    public void A_step_waiting_for_a_window_or_a_program_names_the_rule(string type, string key)
    {
        var failure = Subject().Timeout.For(Step(type, Fields.Text("window", "")));

        Assert.Equal(key, failure.Key);
    }

    [Fact]
    public void A_wait_until_names_the_condition_it_waited_for()
    {
        var failure = Subject().Timeout.For(Step("control.waitUntil", Fields.Text("condition", "1 > 0")));

        Assert.Equal(RunKeys.WaitTimeout, failure.Key);
        Assert.Equal("1 > 0", failure.Detail);
    }

    [Fact]
    public void A_field_that_cannot_be_read_falls_back_to_being_slow()
    {
        var (timeout, _) = Subject();

        var failure = timeout.For(Step("ocr.clickText", Fields.Computed("queryText", Formula.Name("nope"))));

        Assert.Equal(RunKeys.Timeout, failure.Key);
        Assert.Equal("1250", failure.Detail);
    }

    [Fact]
    public void A_failure_key_that_already_says_what_was_waited_for_is_kept()
    {
        foreach (var key in new[]
                 {
                     RunKeys.Timeout, RunKeys.WaitColorTimeout, RunKeys.WaitTimeout,
                     RunKeys.ImageNotFound, RunKeys.DetectNotFound, RunKeys.ScreenNotStable,
                     RunKeys.TextNotFound, RunKeys.ElementNotFound, RunKeys.ClipboardTimeout,
                     RunKeys.ColourNotFound, RunKeys.ProcessTimeout, RunKeys.ProgramNotFound,
                     RunKeys.WindowTimeout, RunKeys.CommandTimeout, RunKeys.BrowserNotFound,
                     RunKeys.BrowserNoNewTab,
                 })
        {
            Assert.True(StepTimeout.Says(key), key);
        }

        Assert.False(StepTimeout.Says("Run.NobodyWritesThis"));
        Assert.False(StepTimeout.Says(string.Empty));
    }

    [Fact]
    public void A_wait_for_a_picture_names_the_pictures_it_looked_for()
    {
        var (timeout, _) = Subject();

        foreach (var type in new[] { "vision.waitImage", "vision.clickImage" })
        {
            var failure = timeout.For(Step(type, Fields.Rows("images",
                new System.Collections.Generic.Dictionary<string, string>
                {
                    ["image"] = "shot",
                })));

            Assert.Equal(RunKeys.ImageNotFound, failure.Key);
        }
    }
}
