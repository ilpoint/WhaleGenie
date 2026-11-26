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


namespace WhaleGenie.Core.Tests;

// What a search found: the picture, where it sits, and which hit was taken.
public partial class RunLookTests
{

    private static async Task<RunResult> Run(ExecutableStep step, FakeDeviceLayer devices,
        Watched? looks = null)
        => await RunAll([step], devices, looks);

    private static async Task<RunResult> RunAll(ExecutableStep[] steps, FakeDeviceLayer devices,
        Watched? looks = null)
        => await new MacroRunner(new VariableStore(), new SilentRunHost(), devices, 1, null, looks)
            .RunAsync(steps);

    [Fact]
    public async Task A_picture_that_was_found_is_handed_over_with_where_it_was()
    {
        var devices = Screen(match: new ImageMatch(0.95, new ScreenPoint(30, 40), new ScreenSize(2, 2)));
        var looks = new Watched();

        await Run(Find(), devices, looks);

        var look = Assert.Single(looks.Seen);
        Assert.Equal("vision.findImage", look.StepType);
        Assert.Equal("k3f9", look.StepId);
        Assert.Equal(LookKind.Template, look.Kind);
        Assert.Equal(new ScreenPoint(0, 0), look.Origin);
        Assert.Equal(200, look.Frame.Width);
        Assert.Equal(2, look.Boxes.Count);
        Assert.Equal(LookRole.Area, look.Boxes[0].Role);
        Assert.Equal(LookRole.Hit, look.Boxes[1].Role);
        Assert.Equal(new ScreenPoint(30, 40), look.Boxes[1].Match.Location);
        Assert.Equal(0.95, look.Boxes[1].Match.Score);
        Assert.Equal(2, look.ChosenIndex);
        Assert.NotNull(look.Needle);
        Assert.Equal(0.9, look.Minimum);
    }

    [Fact]
    public async Task A_search_inside_a_region_is_reported_in_screen_pixels()
    {
        var devices = Screen(match: new ImageMatch(0.9, new ScreenPoint(5, 6), new ScreenSize(2, 2)));
        var looks = new Watched();

        await Run(Find(regions: [(10, 20, 50, 40)]), devices, looks);

        var look = Assert.Single(looks.Seen);
        Assert.Equal(new ScreenPoint(10, 20), look.Origin);
        Assert.Equal(50, look.Frame.Width);
        Assert.Equal(40, look.Frame.Height);
        Assert.Equal(new ScreenPoint(10, 20), look.Boxes[0].Match.Location);
        Assert.Equal(50, look.Boxes[0].Match.Size.Width);

        // The hit was found five across and six down inside the region, so on screen it sits at
        // fifteen and twenty-six: the position a mark has to have to be drawn over the right place.
        Assert.Equal(new ScreenPoint(15, 26), look.Boxes[1].Match.Location);
    }

    [Fact]
    public async Task A_step_that_found_nothing_still_hands_over_what_it_looked_at()
    {
        var devices = Screen();
        var looks = new Watched();

        var result = await Run(Find(), devices, looks);

        Assert.True(result.Succeeded, result.Key);
        var look = Assert.Single(looks.Seen);
        Assert.Equal(0, look.ChosenIndex);
        Assert.Equal(LookRole.Area, Assert.Single(look.Boxes).Role);
    }

    /// <summary>
    /// A step may list several reference pictures — the same button drawn differently from one
    /// screen to the next — and the first one that turns up is the one it goes with.
    /// </summary>
    [Fact]
    public async Task Each_reference_picture_is_tried_in_turn_until_one_turns_up()
    {
        var first = new ImageFrame(3, 3, new byte[36]);
        var second = new ImageFrame(4, 4, new byte[64]);
        var devices = Screen();
        devices.Vision.Pictures[@"C:\images\first.png"] = first;
        devices.Vision.Pictures[@"C:\images\second.png"] = second;
        devices.Vision.PictureAnswers = (needle, _) => needle.Width == second.Width
            ? [new ImageMatch(0.95, new ScreenPoint(30, 40), new ScreenSize(2, 2))]
            : [];

        var looks = new Watched();
        var step = Step("vision.findImage", Pictures(@"C:\images\first.png", @"C:\images\second.png"),
            Param("confidence", "90"), Hit("match", "match"));

        var result = await Run(step, devices, looks);

        Assert.Equal(RunStatus.Completed, result.Status);
        Assert.Equal(2, devices.Vision.Queries.Count);
        var look = Assert.Single(looks.Seen);
        Assert.Equal(2, look.PictureNumber);
        Assert.Equal(2, look.PictureCount);
        Assert.Equal(@"C:\images\second.png", look.Looking);

        // The picture that turned up is the one shown beside the answer, so the window says which
        // of the several was the one that matched.
        Assert.Equal(second, look.Needle);
        Assert.Equal(LookRole.Hit, look.Boxes[^1].Role);
        Assert.Equal(new ScreenPoint(30, 40), look.Boxes[^1].Match.Location);
    }

    /// <summary>
    /// "The third one" counts the hits of the picture that turned up: a picture that was not there
    /// contributes nothing to the count, so listing several does not shift which hit a step takes.
    /// </summary>
    [Fact]
    public async Task Which_hit_is_taken_is_counted_inside_the_picture_that_turned_up()
    {
        var first = new ImageFrame(3, 3, new byte[36]);
        var second = new ImageFrame(4, 4, new byte[64]);
        var devices = Screen();
        devices.Vision.Pictures[@"C:\images\first.png"] = first;
        devices.Vision.Pictures[@"C:\images\second.png"] = second;
        devices.Vision.PictureAnswers = (needle, _) => needle.Width == second.Width
            ?
            [
                new ImageMatch(0.95, new ScreenPoint(10, 10), new ScreenSize(2, 2)),
                new ImageMatch(0.95, new ScreenPoint(10, 20), new ScreenSize(2, 2)),
                new ImageMatch(0.95, new ScreenPoint(10, 30), new ScreenSize(2, 2)),
            ]
            : [];

        var looks = new Watched();
        var step = Step("vision.findImage", Pictures(@"C:\images\first.png", @"C:\images\second.png"),
            Param("confidence", "90"), Param("matchIndex", "3"), Hit("match", "match"));

        var result = await Run(step, devices, looks);

        Assert.Equal(RunStatus.Completed, result.Status);
        var look = Assert.Single(looks.Seen);
        Assert.Equal(2, look.PictureNumber);

        // "The third one" counts the hits of the picture that turned up. The picture tried before
        // it had none, so its turn contributes nothing to the count; the mark taken is the third
        // of the second picture's three hits.
        Assert.Equal(3, look.Boxes.Count(box => box.Role is LookRole.Hit or LookRole.Candidate));
        Assert.Equal(LookRole.Hit, look.Boxes[^1].Role);
        Assert.Equal(new ScreenPoint(10, 30), look.Boxes[^1].Match.Location);
    }

    /// <summary>
    /// A picture that is listed but not filled in is one there is nothing to look for, and a step
    /// that lists none at all cannot look for anything — it says so rather than searching for
    /// whatever it happens to have.
    /// </summary>
    [Fact]
    public async Task A_search_with_no_reference_picture_says_what_is_missing()
    {
        var devices = Screen();
        var step = Step("vision.findImage", Pictures("  "), Param("confidence", "90"),
            Hit("match", "match"));

        var result = await Run(step, devices);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.MissingImage", result.Key);
        Assert.Empty(devices.Vision.Queries);
    }
}
