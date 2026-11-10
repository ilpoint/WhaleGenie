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

// Aiming at the picture: the wheel, the markers and the selection a click drops.
public partial class LookPickTests
{

    [Theory]
    [InlineData("12,-4")]
    [InlineData("12,-4,red")]
    [InlineData("1,2,#12345")]
    [InlineData("1,2,#GGGGGG")]
    [InlineData("1+,2,#FFFFFF")]
    public void A_line_that_is_not_two_numbers_and_a_colour_is_refused(string written)
        => Assert.False(ColourPointLine.TryParse(written, out _));

    /// <summary>
    /// 整屏铺进一扇窗里时，一个像素只有零点几个点宽：滚轮把画面放大，才瞄得到。
    /// </summary>
    [Fact]
    public void The_wheel_makes_the_picture_bigger_to_aim_at()
    {
        Ui.Run(() =>
        {
            var area = Picture(200, 120, new PixelColor(0x10, 0x20, 0x30));
            var look = Look(area, new ScreenPoint(0, 0),
                new ImageMatch(0.95, new ScreenPoint(10, 10), new ScreenSize(8, 8)));
            var window = new LookPickWindow(look, area, new ScreenPoint(0, 0),
                LookPickMode.Offset, new ScreenPoint(14, 14));
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var before = window.ZoomLevel;
            window.MouseWheel(new Point(200, 300), new Vector(0, 1), RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.True(window.ZoomLevel > before,
                $"滚轮之后还是 {window.ZoomLevel:0.00}，放大前是 {before:0.00}");
            window.Close();
        });
    }

    /// <summary>
    /// 按住图上的圆点拖一下，就是把那一条挪到放手的地方：不用先选中、再点一次。
    /// </summary>
    [Fact]
    public void Dragging_a_marker_moves_that_line()
    {
        Ui.Run(() =>
        {
            var frame = Picture(20, 20, new PixelColor(0x10, 0x20, 0x30),
                (5, 6, new PixelColor(0xAA, 0xBB, 0xCC)),
                (7, 8, new PixelColor(0x11, 0x22, 0x33)));
            var look = Look(frame, new ScreenPoint(0, 0),
                new ImageMatch(0.95, new ScreenPoint(0, 0), new ScreenSize(20, 20)));
            var window = new LookPickWindow(look, frame, new ScreenPoint(0, 0),
                LookPickMode.Colours, new ScreenPoint(10, 10));
            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.Pick(new ScreenPoint(5, 6));
            window.Pick(new ScreenPoint(7, 8));

            var board = window.FindControl<Canvas>("Board")!;
            var zoom = window.ZoomLevel;
            var from = board.TranslatePoint(new Point(5 * zoom, 6 * zoom), window)!.Value;
            var to = board.TranslatePoint(new Point(7 * zoom, 8 * zoom), window)!.Value;

            window.MouseDown(from, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseMove(to, RawInputModifiers.LeftMouseButton);
            window.MouseUp(to, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();

            // 第一条被拖到第二条那一处，条数没变。
            Assert.Equal(2, window.Added.Count);
            Assert.Equal(window.Added[1], window.Added[0]);
            window.Close();
        });
    }

    /// <summary>
    /// 取消选中之后，再点画面就是加一条新的：空白处那一下确实是"放下这条"。
    /// </summary>
    [Fact]
    public void Dropping_the_selection_makes_the_next_click_add_another_line()
    {
        Ui.Run(() =>
        {
            var frame = Picture(20, 20, new PixelColor(0x10, 0x20, 0x30),
                (5, 6, new PixelColor(0xAA, 0xBB, 0xCC)),
                (7, 8, new PixelColor(0x11, 0x22, 0x33)));
            var look = Look(frame, new ScreenPoint(0, 0),
                new ImageMatch(0.95, new ScreenPoint(0, 0), new ScreenSize(20, 20)));
            var window = new LookPickWindow(look, frame, new ScreenPoint(0, 0),
                LookPickMode.Colours, new ScreenPoint(10, 10));
            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.Pick(new ScreenPoint(5, 6));
            window.PickLine(0);
            window.ClearSelection();
            window.Pick(new ScreenPoint(7, 8));

            Assert.Equal(2, window.Added.Count);
            window.Close();
        });
    }

    /// <summary>
    /// 图上的圆点带序号，和右侧列表对得上：靠数一串同色的圆点是认不出哪条是哪条的。
    /// </summary>
    [Fact]
    public void Every_marker_carries_the_number_of_its_line()
    {
        Ui.Run(() =>
        {
            var frame = Picture(20, 20, new PixelColor(0x10, 0x20, 0x30),
                (5, 6, new PixelColor(0xAA, 0xBB, 0xCC)),
                (7, 8, new PixelColor(0x11, 0x22, 0x33)));
            var look = Look(frame, new ScreenPoint(0, 0),
                new ImageMatch(0.95, new ScreenPoint(0, 0), new ScreenSize(20, 20)));
            var window = new LookPickWindow(look, frame, new ScreenPoint(0, 0),
                LookPickMode.Colours, new ScreenPoint(10, 10));
            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.Pick(new ScreenPoint(5, 6));
            window.Pick(new ScreenPoint(7, 8));

            var numbers = window.FindControl<Canvas>("Board")!.Children
                .OfType<TextBlock>().Where(text => text.Name == "MarkerNumber")
                .Select(text => text.Text).ToList();

            Assert.Equal(["1", "2"], numbers);
            window.Close();
        });
    }
}
