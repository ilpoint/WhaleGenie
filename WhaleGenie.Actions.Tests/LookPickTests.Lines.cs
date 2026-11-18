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

// A line of the pattern: whole area, moved, retyped, and what it is read as.
public partial class LookPickTests
{
    /// <summary>
    /// 可点的画面是这一步读的整块地方，不是它搜索的那一小块：点在命中框外面照样能落到屏幕上，
    /// 这正是"往旁边挪一点"要的那一下。
    /// </summary>
    [Fact]
    public void The_whole_area_can_be_picked_on_not_only_the_part_that_was_searched()
    {
        Ui.Run(() =>
        {
            var area = Picture(60, 40, new PixelColor(0x10, 0x20, 0x30),
                (55, 35, new PixelColor(0xAA, 0xBB, 0xCC)));
            var look = Look(Picture(20, 20, new PixelColor(0x10, 0x20, 0x30)),
                new ScreenPoint(10, 10),
                new ImageMatch(0.95, new ScreenPoint(10, 10), new ScreenSize(10, 10)));

            var window = new LookPickWindow(look, area, new ScreenPoint(100, 200),
                LookPickMode.Offset, new ScreenPoint(115, 215));

            // 画面的右下角远在命中框之外，仍然是一个可以点的屏幕位置。
            Assert.Equal(new ScreenPoint(160, 240), window.PointAt(new ScreenPoint(60, 40)));
            Assert.Equal(new ScreenPoint(45, 25),
                window.OffsetAt(window.PointAt(new ScreenPoint(60, 40))));
            window.Close();
        });
    }

    /// <summary>
    /// 一条已经加进去的线要能改：选中它、在图上点一下就挪过去；也可以在框里直接改写。
    /// 改完列表跟着变，条数不变。
    /// </summary>
    [Fact]
    public void A_picked_line_can_be_moved_and_retyped()
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

            // 选中第一条，再点画面：它挪到那里，第二条不动。
            window.PickLine(0);
            window.Pick(new ScreenPoint(7, 8));

            Assert.Equal(["-3,-2,#112233", "-3,-2,#112233"], window.Added);

            // 框里改：算式也算得出来。
            window.ApplyEntry(0, "12+3,-4,#FF0000");
            Assert.Equal(["15,-4,#FF0000", "-3,-2,#112233"], window.Added);

            // 删掉一条，剩下的往前挪一格。
            window.RemoveEntry(1);
            Assert.Equal(["15,-4,#FF0000"], window.Added);
            window.Close();
        });
    }

    /// <summary>框里写的三种值：算式、负数、大小写不同的颜色，读出来的都是同一种写法。</summary>
    /// <summary>
    /// 一处可以写几种颜色（竖线分开），后面还可以跟着表里的那两列：不能是、单点容差。
    /// 拾取窗口原样把这两列带回来，所以在它里面改一条不会悄悄把表格里写的东西抹掉。
    /// </summary>
    [Theory]
    [InlineData("1,2,#ff0000|#00ff00", "1,2,#FF0000|#00FF00")]
    [InlineData("1,2,#ff0000|#00ff00,1,10", "1,2,#FF0000|#00FF00,1,10")]
    [InlineData("1,2,#FF0000,,12", "1,2,#FF0000,,12")]
    public void A_line_may_hold_several_colours_and_the_two_columns_behind_them(
        string written, string expected)
    {
        Assert.True(ColourPointLine.TryParse(written, out var entry));
        Assert.Equal(expected, entry);
    }

    [Theory]
    [InlineData("12+3,-4,#ff0000", "15,-4,#FF0000")]
    [InlineData(" 1 , 2 , #AABBCC ", "1,2,#AABBCC")]
    [InlineData("(2+3)*2,-(1+1),#00ff00", "10,-2,#00FF00")]
    // 变量原样留下：它值多少要等宏跑起来才知道。
    [InlineData("dx,dy,side", "dx,dy,side")]
    public void A_line_is_read_as_numbers_and_one_colour(string written, string expected)
    {
        Assert.True(ColourPointLine.TryParse(written, out var entry));
        Assert.Equal(expected, entry);
    }
}
