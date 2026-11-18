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

// Picking colours: one line each, and nothing twice.
public partial class LookPickTests
{
    /// <summary>
    /// 颜色从这张图上读，不从"现在的屏幕"上读：写进宏的是这一步看的那一刻，屏幕早就变了。
    /// 同一点再点一次不会多出一条。
    /// </summary>
    [Fact]
    public void Picking_colours_adds_one_line_each_and_nothing_twice()
    {
        Ui.Run(() =>
        {
            var frame = Picture(new PixelColor(0x10, 0x20, 0x30),
                (5, 6, new PixelColor(0xAA, 0xBB, 0xCC)));
            var look = Look(frame, new ScreenPoint(0, 0),
                new ImageMatch(0.95, new ScreenPoint(0, 0), new ScreenSize(20, 20)));
            var window = new LookPickWindow(look, look.Frame, look.Origin, LookPickMode.Colours,
                new ScreenPoint(10, 10));
            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.Pick(new ScreenPoint(5, 6));
            window.Pick(new ScreenPoint(5, 6));
            window.Pick(new ScreenPoint(6, 6));

            Assert.Equal(["-5,-4,#AABBCC", "-4,-4,#102030"], window.Added);
            window.Close();
        });
    }
}
