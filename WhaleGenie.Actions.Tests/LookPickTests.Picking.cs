using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// Picking a place on the picture, and the offset it becomes from what was found.
public partial class LookPickTests
{
    /// <summary>
    /// 图上的一点加画面的原点，就是屏幕上的那一点：拾取器底下那张图是这一步看的整块画面，
    /// 而画面自己的左上角并不在屏幕的 (0, 0)。
    /// </summary>
    [Fact]
    public void A_place_on_the_picture_is_a_place_on_the_screen()
    {
        Ui.Run(() =>
        {
            var look = Look(Picture(new PixelColor(0x10, 0x20, 0x30)), new ScreenPoint(100, 200),
                new ImageMatch(0.95, new ScreenPoint(10, 10), new ScreenSize(10, 10)));
            var window = new LookPickWindow(look, look.Frame, look.Origin, LookPickMode.Offset,
                new ScreenPoint(115, 215));

            Assert.Equal(new ScreenPoint(105, 206), window.PointAt(new ScreenPoint(5, 6)));

            // 同一个点相对命中中心的偏移：中心是 (115, 215)，所以往左 10、往上 9。
            Assert.Equal(new ScreenPoint(-10, -9),
                window.OffsetAt(window.PointAt(new ScreenPoint(5, 6))));
            window.Close();
        });
    }

    /// <summary>点在命中中心上，偏移就是 0：这正是"点中间"的那个答案，也是不补偏移时的落点。</summary>
    [Fact]
    public void The_point_picked_is_counted_from_the_middle_of_what_was_found()
    {
        Ui.Run(() =>
        {
            var look = Look(Picture(new PixelColor(0x10, 0x20, 0x30)), new ScreenPoint(0, 0),
                new ImageMatch(0.95, new ScreenPoint(0, 0), new ScreenSize(20, 20)));
            var window = new LookPickWindow(look, look.Frame, look.Origin, LookPickMode.Offset,
                new ScreenPoint(10, 10));
            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.Pick(new ScreenPoint(10, 10));
            Assert.Equal(Strings.Format("LookPick.OffsetAt", 0, 0), Readout(window));

            window.Pick(new ScreenPoint(14, 3));
            Assert.Equal(Strings.Format("LookPick.OffsetAt", 4, -7), Readout(window));
            window.Close();
        });
    }
}
