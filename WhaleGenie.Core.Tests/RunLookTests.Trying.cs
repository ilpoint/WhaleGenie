using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;


namespace WhaleGenie.Core.Tests;

// What a trial hands over, and what it does not do on the way.
public partial class RunLookTests
{
    [Fact]
    public async Task Nothing_is_handed_over_for_a_step_that_does_not_look_at_the_screen()
    {
        var devices = Screen();
        var looks = new Watched();
        var step = Step("control.setVariable", Param("name", "n"), Param("scope", "local"),
            Param("value", "1"));

        await Run(step, devices, looks);

        Assert.Empty(looks.Seen);
        Assert.Empty(looks.Read);
    }

    /// <summary>
    /// Which window a step is reading is said before the picture is taken, because it is the one
    /// question a look cannot answer: a picture says what was there, not where the engine looked for
    /// it. That is what puts a frame around the window a run is reading.
    /// </summary>
    [Fact]
    public async Task The_window_a_step_reads_is_said_before_the_picture_is_taken()
    {
        var devices = Screen();
        devices.Window.Open.Add(new WindowInfo(4242, "Notepad - notes.txt", new ScreenPoint(1000, 500),
            new ScreenSize(800, 600), false, false));
        var looks = new Watched();

        await Run(Step("vision.captureWindow",
            Param("window", new WindowRule(Title: "Notepad").ToJson().ToJsonString()),
            Hit("image", "shot")),
            devices, looks);

        // The window comes with the reading rather than its handle alone, so whoever is drawing the
        // frame has where the window is without asking the desktop a second time.
        var read = Assert.IsType<WindowInfo>(Assert.Single(looks.Read));
        Assert.Equal(4242, read.Handle);
        Assert.Equal(new ScreenPoint(1000, 500), read.Location);
        Assert.Equal(new ScreenSize(800, 600), read.Size);

        // The desktop itself is no window, so a step reading it says so rather than leaving the last
        // window the run happened to read standing as the answer.
        var desktop = new Watched();
        await Run(Capture(1, 2, 5, 6, Hit("image", "shot")), devices, desktop);

        Assert.Null(Assert.Single(desktop.Read));
    }

    [Fact]
    public void Trying_the_looking_of_a_clicking_step_clicks_nothing()
    {
        var devices = Screen(match: new ImageMatch(0.9, new ScreenPoint(30, 40), new ScreenSize(2, 2)));
        var step = Step("vision.clickImage", Pictures(@"C:\images\ok.png"),
            Param("confidence", "90"), Param("offsetX", "3"), Param("offsetY", "-2"),
            Param("button", "left"), Hit("match", "match"));

        var outcome = MacroRunner.LookOnce(step, devices);

        Assert.NotNull(outcome.Look);
        var look = outcome.Look!;
        Assert.Equal(LookKind.Template, look.Kind);
        Assert.Equal(LookRole.Hit, look.Boxes[1].Role);
        Assert.DoesNotContain(devices.Calls, call => call.StartsWith("click ", StringComparison.Ordinal));
        Assert.DoesNotContain(devices.Calls, call => call.StartsWith("keyPress", StringComparison.Ordinal));

        // No aiming mark: nothing was acted on, so there is nowhere the step went.
        Assert.DoesNotContain(look.Boxes, box => box.Role == LookRole.Target);
    }

    [Fact]
    public void A_step_that_does_not_look_at_the_screen_has_nothing_to_try()
    {
        var outcome = MacroRunner.LookOnce(Step("control.setVariable", Param("name", "n")),
            Screen());

        Assert.Null(outcome.Look);
        Assert.Equal("Run.NotALookingStep", outcome.Key);

        // The answer is the catalogue's, not a second list kept beside it: that list had drifted, and
        // three steps the catalogue says look at the screen were told they do not.
        foreach (var action in ActionSemanticsCatalog.All)
        {
            Assert.Equal(action.LooksAtScreen, MacroRunner.CanLook(action.Key));

            if (action.LooksAtScreen)
            {
                // Saying a step can be tried is a promise the trial has to keep.
                Assert.NotEqual(RunKeys.NotALookingStep,
                    MacroRunner.LookOnce(Step(action.Key), Screen()).Key);
            }
        }
    }
}
