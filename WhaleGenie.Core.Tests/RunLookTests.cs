using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// What each step that looks at the screen hands over for a debugger to draw: the picture it
/// looked at, where that picture sits, and every mark on it.
/// </summary>
public class RunLookTests
{
    /// <summary>Somewhere to put the looks handed over during a run.</summary>
    private sealed class Watched : IRunLooks
    {
        public List<StepLook> Seen { get; } = [];

        public void Look(StepLook look) => Seen.Add(look);
    }

    private static ExecutableStep Step(string type, params ExecutableParameter[] parameters)
        => new() { Type = type, Id = "k3f9", Parameters = parameters };

    private static ExecutableParameter Param(string name, string text = "")
        => new() { Name = name, Text = text };

    /// <summary>
    /// The pictures a step looks for, written the way the dialog writes them: one row each, tried
    /// in the order they are listed.
    /// </summary>
    private static ExecutableParameter Pictures(params string[] written)
        => new()
        {
            Name = "image",
            Rows = [.. written.Select(text => (IReadOnlyDictionary<string, string>)
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["image"] = text,
                })],
        };

    /// <summary>
    /// A step that looks for a picture, with the places to look given the way the dialog gives
    /// them: one row per rectangle.
    /// </summary>
    private static ExecutableStep Find(string image = @"C:\images\ok.png",
        params (int X, int Y, int Width, int Height)[] regions)
        => Step("vision.findImage", Pictures(image), Param("confidence", "90"),
            new ExecutableParameter
            {
                Name = "region",
                Rows = [.. regions.Select(region => (IReadOnlyDictionary<string, string>)
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["x"] = region.X.ToString(CultureInfo.InvariantCulture),
                        ["y"] = region.Y.ToString(CultureInfo.InvariantCulture),
                        ["width"] = region.Width.ToString(CultureInfo.InvariantCulture),
                        ["height"] = region.Height.ToString(CultureInfo.InvariantCulture),
                    })],
            },
            Param("resultVariable", "match"));

    private static FakeDeviceLayer Screen(int width = 200, int height = 100,
        ImageMatch? match = null)
        => new()
        {
            PrimarySize = new ScreenSize(width, height),
            Match = match,
            Loaded = new ImageFrame(2, 2, new byte[16]),
            Display = new ImageFrame(width, height, new byte[width * height * 4]),
        };

    private static async Task<RunResult> Run(ExecutableStep step, FakeDeviceLayer devices,
        Watched? looks = null)
        => await new MacroRunner(new VariableStore(), new SilentRunHost(), devices, 1, null, looks)
            .RunAsync([step]);

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

        Assert.Equal(RunStatus.Completed, result.Status);
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
        devices.Pictures[@"C:\images\first.png"] = first;
        devices.Pictures[@"C:\images\second.png"] = second;
        devices.PictureAnswers = (needle, _) => needle.Width == second.Width
            ? [new ImageMatch(0.95, new ScreenPoint(30, 40), new ScreenSize(2, 2))]
            : [];

        var looks = new Watched();
        var step = Step("vision.findImage", Pictures(@"C:\images\first.png", @"C:\images\second.png"),
            Param("confidence", "90"), Param("resultVariable", "match"));

        var result = await Run(step, devices, looks);

        Assert.Equal(RunStatus.Completed, result.Status);
        Assert.Equal(2, devices.Queries.Count);
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
        devices.Pictures[@"C:\images\first.png"] = first;
        devices.Pictures[@"C:\images\second.png"] = second;
        devices.PictureAnswers = (needle, _) => needle.Width == second.Width
            ?
            [
                new ImageMatch(0.95, new ScreenPoint(10, 10), new ScreenSize(2, 2)),
                new ImageMatch(0.95, new ScreenPoint(10, 20), new ScreenSize(2, 2)),
                new ImageMatch(0.95, new ScreenPoint(10, 30), new ScreenSize(2, 2)),
            ]
            : [];

        var looks = new Watched();
        var step = Step("vision.findImage", Pictures(@"C:\images\first.png", @"C:\images\second.png"),
            Param("confidence", "90"), Param("matchIndex", "3"), Param("resultVariable", "match"));

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
            Param("resultVariable", "match"));

        var result = await Run(step, devices);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.MissingImage", result.Key);
        Assert.Empty(devices.Queries);
    }

    [Fact]
    public async Task A_wait_that_ran_out_hands_over_the_look_it_gave_up_on()
    {
        var devices = Screen();
        var looks = new Watched();
        var step = Step("vision.waitImage", Pictures(@"C:\images\ok.png"),
            Param("confidence", "90"), Param("timeoutMs", "60"), Param("intervalMs", "10"),
            Param("resultVariable", "match"));

        var result = await Run(step, devices, looks);

        Assert.Equal(RunStatus.Failed, result.Status);
        var look = Assert.Single(looks.Seen);
        Assert.Equal(0, look.ChosenIndex);
        Assert.True(devices.Searches > 1, $"the picture should be looked for more than once, was {devices.Searches}");
    }

    [Fact]
    public async Task What_was_searched_is_put_together_into_one_picture()
    {
        var quiet = Screen(match: new ImageMatch(0.9, new ScreenPoint(1, 1), new ScreenSize(2, 2)));
        var watched = Screen(match: new ImageMatch(0.9, new ScreenPoint(1, 1), new ScreenSize(2, 2)));
        var looks = new Watched();

        await Run(Find(regions: [(10, 20, 30, 40), (50, 10, 30, 40)]), quiet);
        await Run(Find(regions: [(10, 20, 30, 40), (50, 10, 30, 40)]), watched, looks);

        // The picture the marks are drawn on is laid out of the areas themselves, so a step reads
        // each of them once and once only whether or not anybody is watching.
        Assert.Equal(2, quiet.Calls.Count(call => call.StartsWith("capture ", StringComparison.Ordinal)));
        Assert.Equal(2, watched.Calls.Count(call => call.StartsWith("capture ", StringComparison.Ordinal)));

        var look = Assert.Single(looks.Seen);
        Assert.Equal(new ScreenPoint(10, 10), look.Origin);
        Assert.Equal(70, look.Frame.Width);
        Assert.Equal(50, look.Frame.Height);
        Assert.Equal(2, look.Boxes.Count(box => box.Role == LookRole.Area));
    }

    [Fact]
    public async Task The_same_reference_picture_is_read_from_disk_once_and_a_changed_one_is_read_again()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wg-look-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, PngWriter.Encode(new ImageFrame(2, 2, new byte[16])));
        try
        {
            var devices = Screen();
            var runner = new MacroRunner(new VariableStore(), new SilentRunHost(), devices);

            await runner.RunAsync([Find(path)]);
            await runner.RunAsync([Find(path)]);
            Assert.Single(devices.Calls, call => call.StartsWith("load ", StringComparison.Ordinal));

            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(5));
            await runner.RunAsync([Find(path)]);
            Assert.Equal(2, devices.Calls.Count(call => call.StartsWith("load ", StringComparison.Ordinal)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task A_click_is_handed_over_with_the_place_it_aimed_at()
    {
        var devices = Screen(match: new ImageMatch(0.9, new ScreenPoint(30, 40), new ScreenSize(2, 2)));
        var looks = new Watched();
        var step = Step("vision.clickImage", Pictures(@"C:\images\ok.png"),
            Param("confidence", "90"), Param("offsetX", "3"), Param("offsetY", "-2"),
            Param("timeoutMs", "0"), Param("button", "left"));

        await Run(step, devices, looks);

        var look = Assert.Single(looks.Seen);
        var aimed = look.Boxes[^1];
        Assert.Equal(LookRole.Target, aimed.Role);

        // The middle of a two by two hit at thirty, forty is thirty-one, forty-one; the step moved
        // three across and two up from there.
        Assert.Equal(new ScreenPoint(34, 39), aimed.Match.Location);
        Assert.Equal(look.Boxes.Count, look.ChosenIndex);
    }

    [Fact]
    public async Task Every_line_that_was_read_is_handed_over_and_not_only_the_one_that_matched()
    {
        var devices = Screen();
        devices.Spans =
        [
            new TextSpan("Save", new ScreenPoint(5, 6), new ScreenSize(20, 8), 30),
            new TextSpan("Cancel", new ScreenPoint(40, 6), new ScreenSize(20, 8), 12),
        ];
        var looks = new Watched();
        var step = Step("ocr.findText", Param("text", "Save"), Param("matchMode", "contains"),
            Param("minScore", "0"), Param("resultVariable", "match"));

        await Run(step, devices, looks);

        var look = Assert.Single(looks.Seen);
        Assert.Equal(LookKind.Text, look.Kind);
        Assert.Equal(3, look.Boxes.Count);
        Assert.Equal("Save", look.Boxes[1].Label);
        Assert.Equal(LookRole.Hit, look.Boxes[1].Role);
        Assert.Equal("Cancel", look.Boxes[2].Label);
        Assert.Equal(LookRole.Candidate, look.Boxes[2].Role);
        Assert.Equal(2, look.ChosenIndex);
        Assert.Equal(0, look.Minimum);
    }

    [Fact]
    public async Task Reading_a_region_hands_over_the_writing_with_where_it_was_read()
    {
        var devices = Screen();
        devices.Spans = [new TextSpan("Save", new ScreenPoint(5, 6), new ScreenSize(20, 8), 30)];
        var looks = new Watched();
        var step = Step("ocr.recognize", Param("x", "10"), Param("y", "20"), Param("width", "40"),
            Param("height", "30"), Param("language", "auto"), Param("resultVariable", "text"));

        await Run(step, devices, looks);

        var look = Assert.Single(looks.Seen);
        Assert.Equal("Save", look.Note);
        Assert.Equal(new ScreenPoint(15, 26), look.Boxes[1].Match.Location);
        Assert.Equal(30, look.Boxes[1].Match.Score);
    }

    [Fact]
    public async Task A_picture_that_was_taken_is_handed_over_as_the_picture_it_took()
    {
        var devices = Screen();
        var looks = new Watched();
        var step = Step("vision.capture", Param("x", "1"), Param("y", "2"), Param("width", "5"),
            Param("height", "6"), Param("saveTo", "shot"));

        await Run(step, devices, looks);

        var look = Assert.Single(looks.Seen);
        Assert.Equal(LookKind.Capture, look.Kind);
        Assert.Equal(new ScreenPoint(1, 2), look.Origin);
        Assert.Equal(5, look.Frame.Width);
        Assert.Equal(6, look.Frame.Height);
        Assert.Equal(new ScreenPoint(1, 2), Assert.Single(look.Boxes).Match.Location);
        Assert.Equal("shot", look.Looking);
    }

    [Fact]
    public async Task A_watched_pixel_is_handed_over_with_a_square_of_screen_around_it()
    {
        var devices = Screen();
        var looks = new Watched();
        var step = Step("vision.getPixel", Param("x", "50"), Param("y", "25"),
            Param("resultVariable", "color"));

        await Run(step, devices, looks);

        var look = Assert.Single(looks.Seen);
        Assert.Equal(LookKind.Pixel, look.Kind);
        Assert.Equal(2, look.Boxes.Count);
        Assert.Equal(LookRole.Target, look.Boxes[1].Role);
        Assert.Equal(new ScreenPoint(50, 25), look.Boxes[1].Match.Location);
        Assert.Equal(2, look.ChosenIndex);
        Assert.False(look.Frame.IsEmpty);
    }

    [Fact]
    public async Task Nothing_is_handed_over_for_a_step_that_does_not_look_at_the_screen()
    {
        var devices = Screen();
        var looks = new Watched();
        var step = Step("control.setVariable", Param("name", "n"), Param("scope", "local"),
            Param("value", "1"));

        await Run(step, devices, looks);

        Assert.Empty(looks.Seen);
    }

    [Fact]
    public void Trying_the_looking_of_a_clicking_step_clicks_nothing()
    {
        var devices = Screen(match: new ImageMatch(0.9, new ScreenPoint(30, 40), new ScreenSize(2, 2)));
        var step = Step("vision.clickImage", Pictures(@"C:\images\ok.png"),
            Param("confidence", "90"), Param("offsetX", "3"), Param("offsetY", "-2"),
            Param("timeoutMs", "0"), Param("button", "left"), Param("resultVariable", "match"));

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
        Assert.True(MacroRunner.CanLook("vision.findImage"));
        Assert.False(MacroRunner.CanLook("clipboard.writeImage"));
    }

    [Fact]
    public void Trying_the_looking_of_a_wait_does_not_wait()
    {
        var devices = Screen();
        var step = Step("vision.waitImage", Pictures(@"C:\images\ok.png"),
            Param("confidence", "90"), Param("timeoutMs", "60000"), Param("intervalMs", "10"),
            Param("resultVariable", "match"));

        var outcome = MacroRunner.LookOnce(step, devices);

        Assert.NotNull(outcome.Look);
        var look = outcome.Look!;
        Assert.Equal(0, look.ChosenIndex);
        Assert.Equal(1, devices.Searches);
    }
}
