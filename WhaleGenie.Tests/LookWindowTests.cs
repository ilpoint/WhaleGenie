using Avalonia.Controls;
using Avalonia.Threading;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// Where the marks of a look land on the picture it came from. A mark is in screen pixels and the
/// picture has a corner of its own, so the window has to subtract one and scale by the zoom — and
/// a hit found by a colour is one pixel of a whole screen, which has to stay visible anyway.
/// </summary>
public class LookWindowTests
{
    /// <summary>
    /// The button that tries the looking out belongs on the line that says what the step is
    /// looking for, and only on the actions that look at the screen: a picture a step copies
    /// elsewhere is not something to try looking for.
    /// </summary>
    [Theory]
    [InlineData("vision.findImage", "image")]
    [InlineData("vision.waitImage", "image")]
    [InlineData("vision.clickImage", "image")]
    [InlineData("vision.findColor", "color")]
    [InlineData("vision.waitColor", "color")]
    [InlineData("vision.capture", "x")]
    [InlineData("vision.getPixel", "x")]
    [InlineData("ocr.recognize", "x")]
    [InlineData("ocr.findText", "text")]
    [InlineData("ocr.clickText", "text")]
    [InlineData("clipboard.writeImage", "")]
    [InlineData("window.activate", "")]
    [InlineData("input.mouseClick", "")]
    public void The_button_that_tries_the_looking_out_sits_on_what_is_being_looked_for(
        string key, string expected)
    {
        Ui.Run(() =>
        {
            var window = new AddActionWindow(null, ActionCatalog.Definitions,
                VariableChoicesForChecks.Named("match"), []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.SelectAction(key);
            Dispatcher.UIThread.RunJobs();

            var rows = viewModel.Rows.Concat(viewModel.AdvancedRows)
                .Where(row => row.ShowsLook)
                .ToList();

            if (expected.Length == 0)
            {
                Assert.Empty(rows);
                return;
            }

            Assert.Equal(expected, Assert.Single(rows).First.Definition.Name);
        });
    }

    [Fact]
    public void A_mark_is_placed_inside_the_picture_the_step_looked_at()
    {
        var place = LookGeometry.Place(
            new ScreenPoint(100, 200), new ScreenPoint(130, 250), new ScreenSize(20, 10), 1);

        Assert.Equal(new LookRect(30, 50, 20, 10), place);
    }

    /// <summary>
    /// Two things are worth saving off this window and they are not the same thing: the picture
    /// with the marks on it, to show somebody what was seen, and the picture without them, to cut
    /// a fresh reference picture out of — marks drawn on a template come along into it and make it
    /// match only itself.
    /// </summary>
    [Fact]
    public void The_picture_can_be_saved_with_or_without_the_marks_on_it()
    {
        Ui.Run(() =>
        {
            var window = new LookWindow(Found());

            Assert.Equal(Strings.Get("Look.Save"), window.FindControl<Button>("SaveMarked")?.Content);
            Assert.Equal(Strings.Get("Look.SaveRaw"), window.FindControl<Button>("SaveRaw")?.Content);

            // The picture itself, and the one mark the step went with.
            Assert.Equal(2, window.SavedBoard(1, marks: true).Children.Count);
            Assert.Single(window.SavedBoard(1, marks: false).Children);
        });
    }

    /// <summary>A step that found its picture, so the window has a mark to draw over it.</summary>
    private static StepLook Found() => new()
    {
        StepId = "k3f9",
        StepType = "vision.findImage",
        Kind = LookKind.Template,
        Frame = new ImageFrame(20, 12, new byte[20 * 12 * 4]),
        Origin = new ScreenPoint(0, 0),
        Looking = "ok.png",
        Boxes =
        [
            new LookBox(new ImageMatch(0.97, new ScreenPoint(4, 3), new ScreenSize(6, 4)), LookRole.Hit),
        ],
        ChosenIndex = 1,
    };

    [Fact]
    public void The_zoom_is_applied_to_the_mark_as_well_as_to_the_picture()
    {
        var place = LookGeometry.Place(
            new ScreenPoint(0, 0), new ScreenPoint(10, 10), new ScreenSize(4, 4), 2.5);

        Assert.Equal(new LookRect(25, 25, 10, 10), place);
    }

    [Fact]
    public void A_one_pixel_hit_is_enlarged_so_that_it_can_be_seen_and_stays_where_it_was()
    {
        var visible = LookGeometry.Visible(new LookRect(40, 60, 1, 1), 9);

        Assert.Equal(9, visible.Width);
        Assert.Equal(9, visible.Height);

        // The middle is still the middle: the mark grew around the pixel, not off to one side.
        Assert.Equal(40.5, visible.X + (visible.Width / 2));
        Assert.Equal(60.5, visible.Y + (visible.Height / 2));
    }

    [Fact]
    public void A_mark_bigger_than_the_smallest_one_is_left_as_it_is()
    {
        var visible = LookGeometry.Visible(new LookRect(5, 6, 20, 10), 9);

        Assert.Equal(new LookRect(5, 6, 20, 10), visible);
    }

    [Fact]
    public void Fitting_shows_the_whole_picture_without_enlarging_a_small_one()
    {
        Assert.Equal(0.5, LookGeometry.Fit(1000, 500, 500, 250));
        Assert.Equal(1, LookGeometry.Fit(100, 50, 500, 250));
        Assert.Equal(1, LookGeometry.Fit(0, 0, 500, 250));
    }

    [Fact]
    public void Zooming_in_and_out_stays_inside_what_the_view_allows()
    {
        Assert.True(LookGeometry.Step(1, true) > 1);
        Assert.True(LookGeometry.Step(1, false) < 1);
        Assert.Equal(LookGeometry.LargestZoom, LookGeometry.Step(LookGeometry.LargestZoom, true));
        Assert.Equal(LookGeometry.SmallestZoom, LookGeometry.Step(LookGeometry.SmallestZoom, false));
    }

    /// <summary>
    /// The click point a step uses is measured from the middle of the picture: that is where a
    /// click lands when the step asks for no offset, and where a picked point has to be counted
    /// from for the numbers to mean what the field says.
    /// </summary>
    [Theory]
    [InlineData(40, 20, 35, 5, 15, -5)]
    [InlineData(40, 20, 20, 10, 0, 0)]
    [InlineData(41, 21, 0, 0, -20, -10)]
    public void A_point_on_the_picture_becomes_an_offset_from_its_middle(
        int width, int height, int x, int y, int offsetX, int offsetY)
    {
        var offset = LookGeometry.Offset(width, height, new ScreenPoint(x, y));

        Assert.Equal(new ScreenPoint(offsetX, offsetY), offset);
    }

    [Fact]
    public void Clicking_outside_the_picture_is_a_click_at_its_edge()
    {
        // Drawn four times its size, a click on the far corner is still inside the picture.
        Assert.Equal(new ScreenPoint(9, 4), LookGeometry.Inside(10, 5, 4, 38, 18));
        Assert.Equal(new ScreenPoint(9, 4), LookGeometry.Inside(10, 5, 4, 400, 400));
        Assert.Equal(new ScreenPoint(0, 0), LookGeometry.Inside(10, 5, 4, -50, -50));
    }

    /// <summary>
    /// The offset is only worth picking on an action that looks for a picture: a step that clicks
    /// writing has an offset too, but there is nothing to point at.
    /// </summary>
    [Theory]
    [InlineData("vision.clickImage", true)]
    [InlineData("ocr.clickText", false)]
    [InlineData("input.mouseClick", false)]
    public void Picking_a_click_point_is_offered_where_there_is_a_picture_to_point_at(
        string key, bool offered)
    {
        Ui.Run(() =>
        {
            var window = new AddActionWindow(null, ActionCatalog.Definitions,
                VariableChoicesForChecks.Named("match"), []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.SelectAction(key);
            Dispatcher.UIThread.RunJobs();

            var pickers = viewModel.Rows.Concat(viewModel.AdvancedRows)
                .Where(row => row.ShowsOffsetPick)
                .ToList();

            Assert.Equal(offered ? 1 : 0, pickers.Count);
            if (offered)
            {
                // The two halves of the offset sit on the one line the button is under.
                Assert.Equal("offsetX", pickers[0].First.Definition.Name);
                Assert.Equal("offsetY", pickers[0].Second?.Definition.Name);
            }
        });
    }
}
