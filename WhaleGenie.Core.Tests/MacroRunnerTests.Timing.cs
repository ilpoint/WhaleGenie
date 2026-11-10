using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Tests;

// How fast a run goes: the speed factor and the slack it gives every wait.
public partial class MacroRunnerTests
{
    [Fact]
    public async Task A_speed_factor_bends_the_waits_without_rewriting_them()
    {
        var wait = Step("control.delay", Param("ms", "250"));

        var asWritten = await TimeAsync([wait], 1);
        var quicker = await QuickestAsync([wait], 0.1);
        var slower = await TimeAsync([wait], 2);

        Assert.True(asWritten >= 230, $"×1 should wait about 250ms, took {asWritten:0}ms");
        Assert.True(quicker < 200, $"×0.1 should barely wait, took {quicker:0}ms");
        Assert.True(slower >= 480, $"×2 should wait about 500ms, took {slower:0}ms");

        // The macro keeps its own numbers: the factor is only bent in while it runs.
        Assert.Equal("250", wait.Text("ms"));
    }

    [Fact]
    public async Task A_length_of_time_can_be_given_slack_so_it_is_never_the_same_twice()
    {
        // 200ms with 25% of give lands somewhere between 150 and 250, and never on the same
        // number twice: this is what stops a wait from looking like a machine's.
        var varied = Step("control.delay",
            new ExecutableParameter { Name = "ms", Text = "200", Jitter = 0.25m });

        var waits = new List<double>();
        for (var round = 0; round < 5; round++)
        {
            waits.Add(await TimeAsync([varied], 1));
        }

        AssertWaitsAreSpaced(waits, 145, 1000);
        Assert.True(waits.Max() - waits.Min() > 10,
            $"every wait came out about the same: {string.Join(", ", waits.Select(w => w.ToString("0")))}");

        // The macro keeps the number it was written with; the give only bends it while it runs.
        Assert.Equal("200", varied.Text("ms"));
    }

    [Fact]
    public async Task The_slack_of_a_time_is_bent_with_it_by_the_speed_factor()
    {
        // The factor bends the whole wait, give included: 250 ±20% at ×0.1 is at most 30ms.
        var varied = Step("control.delay",
            new ExecutableParameter { Name = "ms", Text = "250", Jitter = 0.2m });

        var quickest = await QuickestAsync([varied], 0.1, rounds: 8);

        Assert.True(quickest < 40, $"×0.1 should barely wait, took {quickest:0}ms");
    }

    [Fact]
    public async Task A_speed_factor_also_bends_the_pauses_around_a_step()
    {
        var paused = new ExecutableStep
        {
            Type = "control.log",
            Parameters = [Param("message", "hi")],
            Meta = new StepMeta { DelayAfterMs = 250 },
        };

        var plain = await TimeAsync([paused], 1);
        var quicker = await QuickestAsync([paused], 0.1);

        Assert.True(plain >= 230, $"the pause should have happened, took {plain:0}ms");
        Assert.True(quicker < 200, $"the pause should have shrunk, took {quicker:0}ms");
    }

    [Fact]
    public void A_speed_factor_outside_the_allowed_range_is_brought_back_in()
    {
        // Nothing a slip of the hand can do should stop a macro dead or drag it out for ever.
        Assert.Equal(10, new MacroRunner(new VariableStore(), delayScale: 500).DelayScale);
        Assert.Equal(0.1, new MacroRunner(new VariableStore(), delayScale: 0).DelayScale);
        Assert.Equal(1, new MacroRunner(new VariableStore(), delayScale: double.NaN).DelayScale);
        Assert.Equal(1, new MacroRunner(new VariableStore()).DelayScale);
    }
}
