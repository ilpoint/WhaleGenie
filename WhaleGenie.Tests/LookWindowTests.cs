using Avalonia.Threading;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
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
}
