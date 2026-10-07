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
}
