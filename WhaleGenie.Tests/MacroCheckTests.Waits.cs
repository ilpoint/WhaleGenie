using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;


namespace WhaleGenie.Tests;

// Waits a background run has to answer for, and what a waiting action needs to say.
public partial class MacroCheckTests
{
    /// <summary>
    /// A wait with no timeout is a legitimate "until it happens". Retrying it, or putting it under
    /// a block with a clock, turns it into a step whose retry or outer budget can never shorten
    /// the wait, so the editor says so before the run hangs.
    /// </summary>
    [Fact]
    public void A_wait_without_a_timeout_is_reported_when_a_retry_or_outer_clock_relies_on_it()
    {
        var retried = Step("vision.waitImage");
        retried.Meta = new StepMeta { RetryCount = 1 };
        var problem = Assert.Single(Inspect(retried));
        Assert.Same(retried, problem.Step);
        Assert.Equal(Strings.Get("Editor.Problem.WaitWithoutTimeout"), problem.Message);

        var waiting = Step("vision.waitImage");
        var bounded = Step("control.repeat", Text("times", "1"), Steps("body", waiting));
        bounded.Meta = new StepMeta { TimeoutMs = 1_000 };
        Assert.Single(Inspect(bounded));

        // Neither answer on its own is a reason to mark the step: waiting for ever is legal, and
        // a timeout of its own is exactly what the check asks for.
        Assert.Empty(Inspect(Step("vision.waitImage")));
        var timed = Step("vision.waitImage");
        timed.Meta = new StepMeta { RetryCount = 1, TimeoutMs = 1_000 };
        Assert.Empty(Inspect(timed));
    }

    /// <summary>
    /// The list of waiting actions is the engine's own: every action the catalogue says waits is
    /// judged, and an action that does not wait is never asked for a timeout.
    /// </summary>
    [Fact]
    public void Every_waiting_action_is_judged_from_the_catalogue()
    {
        Ui.Run(() =>
        {
            foreach (var action in ActionCatalog.Definitions.Where(action => action.Waits))
            {
                var step = Step(action.Key);
                step.Meta = new StepMeta { RetryCount = 1 };
                Assert.Contains(MacroCheck.Inspect([step], null, [], []),
                    problem => problem.Step == step
                        && problem.Message == Strings.Get("Editor.Problem.WaitWithoutTimeout"));
            }

            var pressing = Pressing();
            pressing.Meta = new StepMeta { RetryCount = 1 };
            Assert.Empty(MacroCheck.Inspect([pressing], null, [], []));
        });
    }
}
