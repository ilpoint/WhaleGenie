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

// Putting the window away, its real size, and fitting it again after a resize.
public partial class LookWindowTests
{
    /// <summary>
    /// The window is put away on its own rather than closed, so what was found can be held up
    /// against the screen it came from and read again afterwards.
    /// </summary>
    /// <remarks>
    /// Putting it away is the system's caption button now (the window is an `AppWindow`), so this
    /// drives the state the button drives rather than clicking a button of ours that no longer
    /// exists. What the check is about is the window keeping its contents, not the button.
    /// </remarks>
    [Fact]
    public void Putting_the_window_away_leaves_it_there_to_come_back_to()
    {
        Ui.Run(() =>
        {
            var window = new LookWindow(Found());
            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.WindowState = WindowState.Minimized;
            Assert.Equal(WindowState.Minimized, window.WindowState);

            // Nothing is lost: the same window comes back, still showing what it found.
            window.WindowState = WindowState.Normal;
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.IsVisible);
        });
    }

    /// <summary>
    /// A wide picture is shown shrunk to fit, and goes back and forth between that and the size its
    /// pixels really are, which is what double-clicking it does.
    /// </summary>
    [Fact]
    public void Double_clicking_the_picture_goes_between_its_real_size_and_fitting()
    {
        Ui.Run(() =>
        {
            var wide = Found() with
            {
                Frame = new ImageFrame(4000, 8, new byte[4000 * 8 * 4]),
            };

            var window = new LookWindow(wide);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var fitted = window.FindControl<TextBlock>("ZoomText")!.Text;
            Assert.NotEqual("100%", fitted);

            window.ToggleRealSize();
            Assert.Equal("100%", window.FindControl<TextBlock>("ZoomText")!.Text);

            // Back to the size that fits. It is the fit worked out again rather than the number
            // from before, because the room the picture has is not the same once it is that big:
            // a scrollbar takes some of it.
            window.ToggleRealSize();
            Assert.NotEqual("100%", window.FindControl<TextBlock>("ZoomText")!.Text);
        });
    }

    /// <summary>
    /// 打开时就该铺满：构造函数跑的时候图片区还没有尺寸，"适应窗口"只好按一对兜底数字算，得出的
    /// 是个猜的缩放。第一次布局过去之后要按真实空间重算一遍。
    /// </summary>
    [Fact]
    public void The_window_opens_fitted_to_the_room_it_really_has()
    {
        Ui.Run(() =>
        {
            var window = new LookWindow(Found() with
            {
                Frame = new ImageFrame(1920, 1080, new byte[1920 * 1080 * 4]),
            });

            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(Zoom(window, 1920, 1080), window.FindControl<TextBlock>("ZoomText")!.Text);
            window.Close();
        });
    }

    /// <summary>
    /// 窗口改小之后缩放跟着重算：窗口小了而画面还是按原来那块地方算的大小，看到的就只是画面的一个角，
    /// 而窗口自己不说这件事。
    /// </summary>
    [Fact]
    public void Making_the_window_smaller_fits_the_picture_again()
    {
        Ui.Run(() =>
        {
            var window = new LookWindow(Found() with
            {
                Frame = new ImageFrame(1920, 1080, new byte[1920 * 1080 * 4]),
            });

            window.Show();
            Dispatcher.UIThread.RunJobs();
            var was = window.FindControl<TextBlock>("ZoomText")!.Text;

            window.Width = 640;
            window.Height = 460;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(Zoom(window, 1920, 1080), window.FindControl<TextBlock>("ZoomText")!.Text);
            Assert.NotEqual(was, window.FindControl<TextBlock>("ZoomText")!.Text);
            window.Close();
        });
    }

    /// <summary>
    /// 按这扇窗现在真实的图片区算出来的缩放，写成窗口上那种百分比，好跟它写出来的那个比。
    /// </summary>
    private static string Zoom(Window window, int width, int height)
    {
        var room = window.FindControl<ScrollViewer>("Scroll")!.Viewport;
        return (LookGeometry.Fit(width, height, room.Width - 8, room.Height - 8) * 100)
            .ToString("0", CultureInfo.CurrentCulture) + "%";
    }
}
