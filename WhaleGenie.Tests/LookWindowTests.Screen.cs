using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// Full screen, the zoom limits, and a one-pixel hit being made visible.
public partial class LookWindowTests
{
    /// <summary>
    /// A picture is read at its own size, and a screenful of hits does not fit in a window: the
    /// window fills the screen and comes back by F11, and Esc goes back to the window before it
    /// closes it.
    /// </summary>
    /// <remarks>
    /// There is no button of its own for this any more: the title bar's maximize is that button now,
    /// and having both would be two controls for one window state.
    /// </remarks>
    [Fact]
    public void The_window_fills_the_screen_and_comes_back()
    {
        Ui.Run(() =>
        {
            var window = new LookWindow(Found());
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Null(window.FindControl<Button>("FullScreen"));

            window.KeyPressQwerty(PhysicalKey.F11, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(WindowState.FullScreen, window.WindowState);

            // Esc is the way back before it is the way out: the full screen is what just happened.
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(WindowState.Normal, window.WindowState);
            Assert.True(window.IsVisible);

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.IsVisible);
        });
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
    public void Fitting_shows_the_whole_picture_and_enlarges_a_small_one()
    {
        Assert.Equal(0.5, LookGeometry.Fit(1000, 500, 500, 250));

        // 参考图只有几十像素宽：留在 100% 就是窗口正中一枚邮票，适应窗口要把它放大。
        Assert.Equal(5, LookGeometry.Fit(100, 50, 500, 250));
        Assert.Equal(LookGeometry.LargestZoom, LookGeometry.Fit(10, 10, 5000, 5000));

        // 没有画面时没有可适应的东西，留在原大。
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
}
