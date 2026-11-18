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

// What a mark says, how the rows are boxed and filtered, and the zoom the pointer asks for.
public partial class LookWindowTests
{
    /// <summary>
    /// 每一条标记都要写出它那块矩形的**中心**：往鼠标动作的起点终点里填的是中心（命中记录自带的
    /// x、y 就是它），而界面上原来只有左上角与宽高，要填那一对数得自己把宽高折半加上去。左上角与
    /// 宽高照旧写着，中心取整用的是引擎那一条算法（奇数宽高落在矩形内偏左上的那一格）。
    /// </summary>
    [Fact]
    public void Every_mark_says_where_its_middle_is()
    {
        var answer = Ui.Run(() =>
        {
            var devices = new ChecksDevices
            {
                Screen = new ScreenForChecks(),
                Ocr = new ReadingForChecks("订单号", "A-1024"),
            };

            var step = new[] { Recognize() }.ToExecutable()[0];
            var look = MacroRunner.LookOnce(step, devices).Look!;

            var window = new LookWindow(look);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var rows = window.FindControl<StackPanel>("Marks")!
                .GetVisualDescendants()
                .OfType<Border>()
                .Where(row => row.Classes.Contains("MarkRow"))
                .Select(row => string.Join(" ", row.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Select(text => text.Text ?? string.Empty)))
                .ToList();

            window.Close();
            return (Middles: look.Boxes
                    .Select(box => $"({box.Match.Center.X}, {box.Match.Center.Y})")
                    .ToList(),
                Rows: rows);
        });

        // 这一份至少有读到的两行字加一处要找的地方，每一条都该有自己那一行。
        Assert.NotEmpty(answer.Middles);
        Assert.Equal(answer.Middles.Count, answer.Rows.Count);

        for (var index = 0; index < answer.Middles.Count; index++)
        {
            Assert.Contains(answer.Middles[index], answer.Rows[index], StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 右侧每一行说两件不同的事：框在屏幕的哪儿（中心、左上角、尺寸），和这一步在那儿读到了什么。
    /// 两件事以前是同一种灰、同一列，读的人分不出哪一行是要复制的文字。现在文字单独装进一个下沉的框，
    /// 前面写「识别文字」；位置上那句仍留在行首。
    /// </summary>
    [Fact]
    public void What_was_read_is_boxed_apart_from_where_the_mark_is()
    {
        var answer = Ui.Run(() =>
        {
            var (window, _, _) = TextWindow();
            var board = window.FindControl<Canvas>("Board")!;
            var rows = window.FindControl<StackPanel>("Marks")!.Children.OfType<Border>().ToList();

            var texts = rows
                .SelectMany(row => row.GetVisualDescendants().OfType<TextBlock>())
                .Select(text => text.Text ?? string.Empty)
                .ToList();

            var caption = Strings.Get("Look.ReadTag");
            var drawn = board.Children.OfType<TextBlock>().Count();
            var toggle = window.FindControl<CheckBox>("LabelsOnPicture");

            window.Close();
            return (Drawn: drawn, Toggle: toggle is null, Caption: caption, Texts: texts);
        });

        // 图上不再写字：字多了就没有地方放，而框里那段才是要看的。
        Assert.Equal(0, answer.Drawn);
        Assert.True(answer.Toggle, "「把文字写在框上」这个开关应该没有了");

        // 读到的字有自己的一行说明，位置那一行另说各的。
        Assert.Contains(answer.Caption, answer.Texts);
        Assert.Contains(answer.Texts, text => text.StartsWith("中心", StringComparison.Ordinal));
        Assert.Contains("A-1024", answer.Texts);
    }

    /// <summary>
    /// 反选要能少选：先全选、再反选就一个不剩，再反选又全回来。筛过的行不参与——看得见的那几行才是
    /// 现在说的「全部」，去动看不见的行只是改了一件没有东西可看的事。
    /// </summary>
    [Fact]
    public void Inverting_turns_the_rows_the_other_way_and_leaves_the_filtered_ones_alone()
    {
        var answer = Ui.Run(() =>
        {
            var (window, _, _) = TextWindow();
            var rows = window.FindControl<StackPanel>("Marks")!.Children.OfType<Border>().ToList();
            var invert = window.FindControl<Button>("InvertMarks")!;

            List<CheckBox> Toggles(IEnumerable<Border> which)
                => [.. which.SelectMany(row => row.GetVisualDescendants().OfType<CheckBox>())];

            var every = Toggles(rows);
            invert.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var afterOne = every.Count(toggle => toggle.IsChecked == true);

            invert.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var afterTwo = every.Count(toggle => toggle.IsChecked == true);

            // 筛到只剩一行，再反选：那一行落选，别的行不动。
            window.FindControl<TextBox>("MarkFilter")!.Text = "A-1024";
            Dispatcher.UIThread.RunJobs();
            invert.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            var shown = rows.Where(row => row.IsVisible).ToList();
            var hidden = rows.Where(row => !row.IsVisible).ToList();
            var shownTicked = Toggles(shown).Count(toggle => toggle.IsChecked == true);
            var hiddenTicked = Toggles(hidden).Count(toggle => toggle.IsChecked == true);
            var hiddenRows = hidden.Count;

            window.Close();
            return (Total: every.Count, AfterOne: afterOne, AfterTwo: afterTwo,
                Shown: shown.Count, ShownTicked: shownTicked, Hidden: hiddenRows, HiddenTicked: hiddenTicked);
        });

        Assert.Equal(answer.Total, answer.AfterTwo);
        Assert.True(answer.AfterOne < answer.AfterTwo, "反选一次应该把勾都去掉");
        Assert.Equal(1, answer.Shown);
        Assert.Equal(0, answer.ShownTicked);
        Assert.Equal(answer.Hidden, answer.HiddenTicked);
    }

    /// <summary>
    /// 滚轮缩放，并且以指针为准：指针底下那一点在缩放前后是同一处。以窗口中心为准的话，正在瞄的那处
    /// 标记会从指针底下跑掉。
    /// </summary>
    [Fact]
    public void The_wheel_zooms_about_the_pointer()
    {
        var answer = Ui.Run(() =>
        {
            var window = new LookWindow(Found() with
            {
                Frame = new ImageFrame(1920, 1080, new byte[1920 * 1080 * 4]),
            });

            window.Show();
            Dispatcher.UIThread.RunJobs();

            var scroll = window.FindControl<ScrollViewer>("Scroll")!;
            var at = new Point(scroll.Bounds.Width / 2, scroll.Bounds.Height / 2);
            var wheel = scroll.TranslatePoint(at, window)!.Value;

            // 先放大一格：适应窗口时画面正好铺满宽度，横向没有可滚的余地，"指针底下那点不动"
            // 就无从谈起。放一格之后两个方向都比窗口大。
            window.MouseWheel(wheel, new Vector(0, 1), RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            var before = window.ZoomLevel;
            var content = scroll.Offset + new Vector(at.X, at.Y);

            window.MouseWheel(wheel, new Vector(0, 1), RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            var after = window.ZoomLevel;
            var now = scroll.Offset + new Vector(at.X, at.Y);

            window.Close();
            return (Before: before, After: after,
                KeptX: content.X * (after / before) - now.X,
                KeptY: content.Y * (after / before) - now.Y);
        });

        Assert.True(answer.After > answer.Before, "往上滚一格该放大");
        Assert.InRange(answer.KeptX, -1.5, 1.5);
        Assert.InRange(answer.KeptY, -1.5, 1.5);
    }

    /// <summary>
    /// 拖动画面：按住左键往左上拽，画面跟着走（滚动位置变大，露出右边与下边）。放大之后画面比窗口大，
    /// 拖动是看另一半的唯一办法。
    /// </summary>
    [Fact]
    public void Dragging_the_picture_moves_it()
    {
        var answer = Ui.Run(() =>
        {
            var window = new LookWindow(Found() with
            {
                Frame = new ImageFrame(1920, 1080, new byte[1920 * 1080 * 4]),
            });

            window.Show();
            Dispatcher.UIThread.RunJobs();

            var scroll = window.FindControl<ScrollViewer>("Scroll")!;
            var board = window.FindControl<Canvas>("Board")!;
            var middle = new Point(scroll.Bounds.Width / 2, scroll.Bounds.Height / 2);
            var at = scroll.TranslatePoint(middle, window)!.Value;

            // 先放大，否则画面正好铺满，没有可挪的余地。
            for (var step = 0; step < 3; step++)
            {
                window.MouseWheel(at, new Vector(0, 1), RawInputModifiers.None);
            }

            Dispatcher.UIThread.RunJobs();

            // 从这里开始，往左上拽，位置该往前走同样多。
            scroll.Offset = new Vector(50, 40);
            var to = new Point(at.X - 30, at.Y - 20);

            window.MouseDown(at, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseMove(to, RawInputModifiers.LeftMouseButton);
            window.MouseUp(to, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();

            var moved = scroll.Offset;
            var cursor = board.Cursor;
            window.Close();
            return (Offset: moved, HasCursor: cursor is not null);
        });

        Assert.InRange(answer.Offset.X, 79, 81);
        Assert.InRange(answer.Offset.Y, 59, 61);
        Assert.True(answer.HasCursor, "画面要有一个可以拖的指针");
    }
}
