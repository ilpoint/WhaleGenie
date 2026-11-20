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

// A wait that ran out, what was searched, and the clicks and lines it read.
public partial class RunLookTests
{
    [Fact]
    public async Task A_wait_that_ran_out_hands_over_the_look_it_gave_up_on()
    {
        var devices = Screen();
        var looks = new Watched();

        // The clock has to outlast more than one look: a wait looks, pauses for the run's own beat,
        // and looks again. What this checks is that it looked more than once, not where the
        // scheduler landed inside two beats — a budget of one beat made the answer the machine's.
        var step = Within(300, Step("vision.waitImage", Pictures(@"C:\images\ok.png"),
            Param("confidence", "90"), Hit("match", "match")));

        var result = await Run(step, devices, looks);

        Assert.Equal(RunStatus.Failed, result.Status);
        var look = Assert.Single(looks.Seen);
        Assert.Equal(0, look.ChosenIndex);
        Assert.True(devices.Vision.Searches > 1, $"the picture should be looked for more than once, was {devices.Vision.Searches}");
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
            Param("button", "left"));

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
        devices.Ocr.Spans =
        [
            new TextSpan("Save", new ScreenPoint(5, 6), new ScreenSize(20, 8), 30),
            new TextSpan("Cancel", new ScreenPoint(40, 6), new ScreenSize(20, 8), 12),
        ];
        var looks = new Watched();
        var step = Step("ocr.findText", Param("queryText", "Save"), Param("matchMode", "contains"),
            Param("minScore", "0"), Hit("match", "match"));

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
        devices.Ocr.Spans = [new TextSpan("Save", new ScreenPoint(5, 6), new ScreenSize(20, 8), 30)];
        var looks = new Watched();
        var step = Recognize(10, 20, 40, 30, Returns("text=text"));

        await Run(step, devices, looks);

        var look = Assert.Single(looks.Seen);
        Assert.Equal("Save", look.Note);
        Assert.Equal(new ScreenPoint(15, 26), look.Boxes[1].Match.Location);
        Assert.Equal(30, look.Boxes[1].Match.Score);
    }
}
