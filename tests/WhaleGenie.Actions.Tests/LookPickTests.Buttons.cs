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

// What the right and middle buttons do over a marker.
public partial class LookPickTests
{
    /// <summary>
    /// 右键属于图上的点：点上有菜单（编辑、删除），空白处什么也不做——画面上的右键若是"再加一条"，
    /// 想改一条时就会先多出一条。
    /// </summary>
    [Fact]
    public void The_right_button_belongs_to_the_dot_under_it()
    {
        Ui.Run(() =>
        {
            var frame = Picture(20, 20, new PixelColor(0x10, 0x20, 0x30),
                (5, 6, new PixelColor(0xAA, 0xBB, 0xCC)));
            var look = Look(frame, new ScreenPoint(0, 0),
                new ImageMatch(0.95, new ScreenPoint(0, 0), new ScreenSize(20, 20)));
            var window = new LookPickWindow(look, frame, new ScreenPoint(0, 0),
                LookPickMode.Colours, new ScreenPoint(10, 10));
            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.Pick(new ScreenPoint(5, 6));

            var board = window.FindControl<Canvas>("Board")!;
            var zoom = window.ZoomLevel;
            var onDot = board.TranslatePoint(new Point(5 * zoom, 6 * zoom), window)!.Value;
            var onEmpty = board.TranslatePoint(new Point(15 * zoom, 15 * zoom), window)!.Value;

            var menu = window.MenuFor(new Point(5 * zoom, 6 * zoom));
            Assert.NotNull(menu);
            // 改、加备选色、删：一处能做的三件事都在这一份菜单里。
            Assert.Equal(3, menu!.Items.Count);

            window.MouseDown(onEmpty, MouseButton.Right, RawInputModifiers.RightMouseButton);
            window.MouseUp(onEmpty, MouseButton.Right, RawInputModifiers.RightMouseButton);
            Dispatcher.UIThread.RunJobs();

            // 空白处那一下既不加条，也不开菜单。
            Assert.Single(window.Added);
            Assert.Null(window.MenuFor(new Point(15 * zoom, 15 * zoom)));

            // 点在圆点上按右键也不加条。
            window.MouseDown(onDot, MouseButton.Right, RawInputModifiers.RightMouseButton);
            window.MouseUp(onDot, MouseButton.Right, RawInputModifiers.RightMouseButton);
            Dispatcher.UIThread.RunJobs();
            Assert.Single(window.Added);
            window.Close();
        });
    }

    /// <summary>
    /// 滚轮键不再新增一处：中键常被当成拖动画面的手势，手一滑就多出一条，而多出来的那条
    /// 不点开列表是看不出来的。只有左键是「点一处」。
    /// </summary>
    [Fact]
    public void The_middle_button_no_longer_adds_a_point()
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

            var board = window.FindControl<Canvas>("Board")!;
            var zoom = window.ZoomLevel;
            var at = board.TranslatePoint(new Point(7 * zoom, 8 * zoom), window)!.Value;

            window.MouseDown(at, MouseButton.Middle, RawInputModifiers.MiddleMouseButton);
            window.MouseUp(at, MouseButton.Middle, RawInputModifiers.MiddleMouseButton);
            Dispatcher.UIThread.RunJobs();

            Assert.Empty(window.Added);

            // 左键照旧：点在同一个像素上就是一整条。
            window.Pick(new ScreenPoint(7, 8));
            Assert.Equal(["-3,-2,#112233"], window.Added);
            window.Close();
        });
    }
}
