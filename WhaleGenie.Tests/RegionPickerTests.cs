using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using WhaleGenie.Core.Devices;
using WhaleGenie.GraphicsCapture;
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
    public void A_window_pick_clamps_a_new_rectangle_to_the_client_area()
    {
        Ui.Run(() =>
        {
            var bounds = new RegionPickerWindow.Region(10, 20, 100, 80);
            var window = new RegionPickerWindow(new ChecksDevices { Screen = new Desk() },
                null, bounds);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.MouseDown(new Point(0, 0), MouseButton.Left);
            window.MouseMove(new Point(200, 200));
            window.MouseUp(new Point(200, 200), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(new RegionPickerWindow.Region(10, 20, 100, 80), window.Picked);
        });
    }

    [Fact]
    public void A_window_pick_clips_an_initial_rectangle_and_keeps_a_move_inside()
    {
        Ui.Run(() =>
        {
            var bounds = new RegionPickerWindow.Region(10, 20, 100, 80);
            var window = new RegionPickerWindow(new ChecksDevices { Screen = new Desk() },
                new RegionPickerWindow.Region(0, 0, 50, 50), bounds);
            _ = window.TakeAsync();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(new RegionPickerWindow.Region(10, 20, 40, 30), window.Picked);

            window.MouseDown(new Point(20, 30), MouseButton.Left);
            window.MouseMove(new Point(200, 200));
            window.MouseUp(new Point(200, 200), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(new RegionPickerWindow.Region(70, 70, 40, 30), window.Picked);
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
    public void The_desk_is_drawn_with_the_rectangle_left_alone()
    {
        Ui.Run(() =>
        {
            var machine = new Desk();
            var window = new RegionPickerWindow(new ChecksDevices { Screen = machine });

            // What every entry point into this picker does first: the desk is taken down as a
            // picture — before the window goes up, or the picture would be the picker itself.
            var taken = window.TakeAsync();
            Dispatcher.UIThread.RunJobs();

            // The desk cannot be seen through a window, so it is drawn in one: a picture of the
            // whole desk, one screen pixel to one pixel of it, laid out over the window that the
            // numbers are counted on.
            var desk = window.FindControl<Image>("Desk")!;
            var picture = Assert.IsType<WriteableBitmap>(desk.Source);
            Assert.Equal((machine.Took.Width, machine.Took.Height),
                (picture.PixelSize.Width, picture.PixelSize.Height));
            Assert.Equal(window.Width, desk.Width, 3);
            Assert.Equal(window.Height, desk.Height, 3);

            // The picture is of the desk the window covers, so a place on it is a place the
            // rectangle's numbers name.
            Assert.Equal((window.Width, window.Height),
                ((double)machine.Took.Width, (double)machine.Took.Height));

            // Nothing drawn yet: the desk is the desk, left exactly as it is. What is being looked
            // for is found on it first, and a screen that is already pushed back is one the pointer
            // has to be hunted for on.
            foreach (var band in Bands)
            {
                Assert.Equal(0d, window.FindControl<Border>(band)!.Height);
            }

            // The pointer is the ordinary one as well: the system's crosshair is a plain black one
            // that disappears over a dark picture.
            window.MouseMove(new Point(500, 400));
            Dispatcher.UIThread.RunJobs();
            Assert.Null(window.Cursor);

            window.MouseDown(new Point(10, 20), MouseButton.Left);
            window.MouseMove(new Point(40, 60));
            window.MouseUp(new Point(40, 60), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            // A band on each side of the rectangle leaves exactly the rectangle at full strength.
            Assert.Equal(20d, window.FindControl<Border>("DimTop")!.Height, 3);
            Assert.Equal(10d, window.FindControl<Border>("DimLeft")!.Width, 3);
            Assert.Equal(40d, Canvas.GetLeft(window.FindControl<Border>("DimRight")!), 3);
            Assert.Equal(60d, Canvas.GetTop(window.FindControl<Border>("DimBottom")!), 3);

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(new RegionPickerWindow.Region(10, 20, 30, 40), window.Confirmed);
            Assert.True(taken.IsCompleted);
        });
    }

    /// <summary>
    /// The bands that push the desk back, one per side of the rectangle.
    /// </summary>
    private static readonly string[] Bands = ["DimTop", "DimBottom", "DimLeft", "DimRight"];

    /// <summary>
    /// A desk of its own: it hands back a picture of whatever it was asked for, the way a real desk
    /// answers a request for the whole of it, and says what it was asked for.
    /// </summary>
    private sealed class Desk : IScreenDevice
    {
        public ScreenLayout Desktop { get; } = new(new ScreenArea(0, 0, 1920, 1080),
            [new ScreenDisplay(new ScreenArea(0, 0, 1920, 1080), true)]);

        /// <summary>The region the last picture was asked for.</summary>
        public (int X, int Y, int Width, int Height) Took { get; private set; }

        public ScreenPoint Cursor => new(0, 0);

        public PixelColor PixelAt(int x, int y) => new(0, 0, 0);

        public ImageFrame Capture(int x, int y, int width, int height)
        {
            Took = (x, y, width, height);
            return new ImageFrame(width, height, new byte[width * height * 4]);
        }

        public ScreenShot Capture(ScreenCaptureRequest request)
            => NullDeviceLayer.Instance.Screen.Capture(request);
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
