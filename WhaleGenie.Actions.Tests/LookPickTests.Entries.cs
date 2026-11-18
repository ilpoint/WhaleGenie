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

// A line added by typing it, and the commas a function in a line keeps.
public partial class LookPickTests
{
    /// <summary>
    /// 不点画面也能添一条：给出数字与颜色即可。新条目接在末尾，同一处不写两遍；有数字时圆点落在
    /// 偏移所指的地方，没有数字（算式）时留在命中中心。
    /// </summary>
    [Fact]
    public void A_line_can_be_added_by_typing_it()
    {
        Ui.Run(() =>
        {
            var frame = Picture(20, 20, new PixelColor(0x10, 0x20, 0x30));
            var look = Look(frame, new ScreenPoint(0, 0),
                new ImageMatch(0.95, new ScreenPoint(0, 0), new ScreenSize(20, 20)));
            var window = new LookPickWindow(look, frame, new ScreenPoint(0, 0),
                LookPickMode.Colours, new ScreenPoint(10, 10));
            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.AppendEntry("2,3,#AABBCC");
            window.AppendEntry("2, 3, #aabbcc");
            window.AppendEntry("dx,dy,side");

            Assert.Equal(["2,3,#AABBCC", "dx,dy,side"], window.Added);
            Assert.Equal([new ScreenPoint(12, 13), new ScreenPoint(10, 10)], window.MarkerPlaces);
            window.Close();
        });
    }

    /// <summary>
    /// 一处可以有几种颜色：同一枚按钮换一套配色画出来，位置没变、颜色变了，那是「一处可以是这样
    /// 也可以是那样」，不是两处。加的时候位置不动，颜色写在竖线后面；同一个颜色不会写第二遍。
    /// </summary>
    [Fact]
    public void A_place_may_be_satisfied_by_more_than_one_colour()
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
            window.AddAlternative(0, "#112233");
            Assert.Equal(["-5,-4,#AABBCC|#112233"], window.Added);

            // 同一个颜色加第二遍：一处只留一份。
            window.AddAlternative(0, "#aabbcc");
            Assert.Equal(["-5,-4,#AABBCC|#112233"], window.Added);

            // 位置没动，圆点也没动：加的是颜色，不是另一处。
            Assert.Equal([new ScreenPoint(5, 6)], window.MarkerPlaces);

            // 再点画面是另一处，不是把颜色加到刚加的那一处上。
            window.Pick(new ScreenPoint(7, 8));
            Assert.Equal(2, window.Added.Count);
            window.Close();
        });
    }
}
