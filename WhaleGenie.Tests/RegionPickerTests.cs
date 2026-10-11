using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// Dragging a rectangle on the screen. The rectangle has to come back in screen pixels whichever
/// way it was dragged, and the picker has to keep it until the user confirms or throw it away on
/// Esc.
/// </summary>
public class RegionPickerTests
{
    [Theory]
    [InlineData(10, 20, 40, 60, 10, 20, 30, 40)]
    [InlineData(40, 60, 10, 20, 10, 20, 30, 40)]
    [InlineData(40, 20, 10, 60, 10, 20, 30, 40)]
    [InlineData(10, 60, 40, 20, 10, 20, 30, 40)]
    public void A_rectangle_is_the_same_whichever_way_it_was_dragged(
        int fromX, int fromY, int toX, int toY, int x, int y, int width, int height)
    {
        var region = RegionPickerWindow.Rectangle(new PixelPoint(fromX, fromY), new PixelPoint(toX, toY));

        Assert.Equal(new RegionPickerWindow.Region(x, y, width, height), region);
    }

    [Fact]
    public void Dragging_on_the_screen_gives_the_rectangle_in_screen_pixels()
    {
        Ui.Run(() =>
        {
            var window = new RegionPickerWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.MouseDown(new Point(10, 20), MouseButton.Left);
            window.MouseMove(new Point(40, 60));
            window.MouseUp(new Point(40, 60), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(new RegionPickerWindow.Region(10, 20, 30, 40), window.Picked);

            // The rectangle stays put until it is confirmed, so it can be corrected.
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(new RegionPickerWindow.Region(10, 20, 30, 40), window.Confirmed);
            Assert.False(window.IsVisible);
        });
    }

    [Fact]
    public void Escape_leaves_without_a_rectangle()
    {
        Ui.Run(() =>
        {
            var window = new RegionPickerWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.MouseDown(new Point(10, 20), MouseButton.Left);
            window.MouseMove(new Point(40, 60));
            window.MouseUp(new Point(40, 60), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.Null(window.Confirmed);
            Assert.False(window.IsVisible);
        });
    }

    [Fact]
    public void Dragging_inside_the_rectangle_moves_it()
    {
        Ui.Run(() =>
        {
            var window = Dragged();

            window.MouseDown(new Point(25, 40), MouseButton.Left);
            window.MouseMove(new Point(35, 50));
            window.MouseUp(new Point(35, 50), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(new RegionPickerWindow.Region(20, 30, 30, 40), window.Picked);
            Assert.True(window.IsVisible);
        });
    }

    [Theory]
    [InlineData(40, 60, 50, 70, 10, 20, 40, 50)]
    [InlineData(40, 20, 50, 10, 10, 10, 40, 50)]
    [InlineData(10, 60, 5, 70, 5, 20, 35, 50)]
    public void Dragging_a_handle_pulls_that_corner_and_leaves_the_other_one(
        int grabX, int grabY, int toX, int toY, int x, int y, int width, int height)
    {
        Ui.Run(() =>
        {
            var window = Dragged();

            window.MouseDown(new Point(grabX, grabY), MouseButton.Left);
            window.MouseMove(new Point(toX, toY));
            window.MouseUp(new Point(toX, toY), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(new RegionPickerWindow.Region(x, y, width, height), window.Picked);
        });
    }

    [Fact]
    public void A_side_pulled_past_the_opposite_one_leaves_the_least_rectangle()
    {
        Ui.Run(() =>
        {
            var window = Dragged();

            // The right side, pulled well past the left one.
            window.MouseDown(new Point(40, 40), MouseButton.Left);
            window.MouseMove(new Point(5, 40));
            window.MouseUp(new Point(5, 40), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(new RegionPickerWindow.Region(10, 20, 4, 40), window.Picked);
        });
    }

    [Fact]
    public void A_click_inside_the_rectangle_takes_it()
    {
        Ui.Run(() =>
        {
            var window = Dragged();

            window.MouseDown(new Point(25, 40), MouseButton.Left);
            window.MouseUp(new Point(25, 40), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(new RegionPickerWindow.Region(10, 20, 30, 40), window.Confirmed);
            Assert.False(window.IsVisible);
        });
    }

    [Fact]
    public void A_click_without_a_drag_leaves_nothing_behind()
    {
        Ui.Run(() =>
        {
            var window = new RegionPickerWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.MouseDown(new Point(50, 50), MouseButton.Left);
            window.MouseUp(new Point(51, 51), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Null(window.Picked);
            Assert.True(window.IsVisible);
        });
    }

    /// <summary>A picker with the rectangle 10,20 30×40 already dragged on it.</summary>
    private static RegionPickerWindow Dragged()
    {
        var window = new RegionPickerWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.MouseDown(new Point(10, 20), MouseButton.Left);
        window.MouseMove(new Point(40, 60));
        window.MouseUp(new Point(40, 60), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        return window;
    }
}
