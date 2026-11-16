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

// Where a mark lands, saving the picture, and the rows of what was read.
public partial class LookWindowTests
{

    /// <summary>The dialog's run button, found the way a user finds it: by what it says.</summary>
    private static Button FindRunButton(Window window)
        => window.GetVisualDescendants().OfType<Button>().Single(button =>
            button.IsEffectivelyVisible
            && Equals(button.Content, Strings.Get("Add.RunStep")));

    [Fact]
    public void A_mark_is_placed_inside_the_picture_the_step_looked_at()
    {
        var place = LookGeometry.Place(
            new ScreenPoint(100, 200), new ScreenPoint(130, 250), new ScreenSize(20, 10), 1);

        Assert.Equal(new LookRect(30, 50, 20, 10), place);
    }

    /// <summary>
    /// Two things are worth saving off this window and they are not the same thing: the picture
    /// with the marks on it, to show somebody what was seen, and the picture without them, to cut
    /// a fresh reference picture out of — marks drawn on a template come along into it and make it
    /// match only itself.
    /// </summary>
    [Fact]
    public void The_picture_can_be_saved_with_or_without_the_marks_on_it()
    {
        Ui.Run(() =>
        {
            var window = new LookWindow(Found());

            Assert.Equal(Strings.Get("Look.Save"), window.FindControl<Button>("SaveMarked")?.Content);
            Assert.Equal(Strings.Get("Look.SaveRaw"), window.FindControl<Button>("SaveRaw")?.Content);

            // The picture itself, and the one mark the step went with.
            Assert.Equal(2, window.SavedBoard(1, marks: true).Children.Count);
            Assert.Single(window.SavedBoard(1, marks: false).Children);
        });
    }

    /// <summary>A step that found its picture, so the window has a mark to draw over it.</summary>
    private static StepLook Found() => new()
    {
        StepId = "k3f9",
        StepType = "vision.findImage",
        Kind = LookKind.Template,
        Frame = new ImageFrame(20, 12, new byte[20 * 12 * 4]),
        Origin = new ScreenPoint(0, 0),
        Looking = "ok.png",
        Boxes =
        [
            new LookBox(new ImageMatch(0.97, new ScreenPoint(4, 3), new ScreenSize(6, 4)), LookRole.Hit),
        ],
        ChosenIndex = 1,
    };

    /// <summary>
    /// 只有从添加动作窗口开的那一份拿得到「再看一次」，因为它手上有那一步与那台机器 —— 画面是刚写下
    /// 的这一步看的，门槛没调对时重看一遍比关掉这扇窗、回去再按一次「运行」近得多。运行调试打开的那
    /// 一份不给：那幅画面属于那一次运行，重看会得到与当时不同的东西。
    /// </summary>
    [Fact]
    public void Only_the_window_that_holds_the_step_looks_again()
    {
        var answer = Ui.RunAsync(async () =>
        {
            var plain = new LookWindow(Found());
            plain.Show();
            Dispatcher.UIThread.RunJobs();
            var withoutStep = plain.FindControl<Button>("Again")!.IsEffectivelyVisible;
            plain.Close();

            var (window, reading, _) = TextWindow();
            var before = reading.Looked;

            window.FindControl<Button>("Again")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var again = await Until(() => reading.Looked > before);

            var shown = window.FindControl<Button>("Again")!.IsEffectivelyVisible;
            window.Close();
            return (shown, withoutStep, before, after: reading.Looked, again);
        });

        Assert.False(answer.withoutStep, "没有步骤的那一份不该有「再看一次」");
        Assert.True(answer.shown, "手上有步骤的那一份该有它");
        Assert.True(answer.again, $"按一次该真的重看一遍：{answer.before} → {answer.after}");
    }

    /// <summary>
    /// 读到的字得能带走：上方的备注框列出这一次读到的全部文字（有上限，识别到的字再多也不会把
    /// 画面挤走），右边每一行标记写出它读到的那一行；「复制」把勾选的行放走，右键一行只放走那一行。
    /// </summary>
    [Fact]
    public void What_was_read_is_listed_and_can_be_copied()
    {
        var answer = Ui.Run(() =>
        {
            var (window, _, clipboard) = TextWindow();

            var note = window.FindControl<TextBox>("NoteLine")!;
            var rows = window.FindControl<StackPanel>("Marks")!
                .GetVisualDescendants().OfType<TextBlock>()
                .Select(text => text.Text ?? string.Empty)
                .ToList();

            window.FindControl<Button>("CopyMarks")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var copied = clipboard.Text;

            // 没读到字的那些：引擎自己那句话是唯一说清发生过什么的地方，这时上面那个框还在。
            var plain = new LookWindow(Found() with { Note = "没有找到参考图。" });
            plain.Show();
            Dispatcher.UIThread.RunJobs();
            var fallback = plain.FindControl<TextBox>("NoteLine")!;
            var fallbackShown = fallback.IsVisible;
            var fallbackText = fallback.Text ?? string.Empty;
            plain.Close();

            window.Close();
            return (NoteShown: note.IsVisible, Rows: rows, Copied: copied,
                FallbackShown: fallbackShown, FallbackText: fallbackText);
        });

        // 读到的字只在右边列一次：同一段字写在两处，看的人要花时间确认哪一处才是。
        Assert.False(answer.NoteShown, "读到的字已经在右边的标记里，上面不该再列一遍");

        // 每一行标记把自己读到的字写出来。
        Assert.Contains("订单号", answer.Rows);
        Assert.Contains("A-1024", answer.Rows);

        // 「复制」放走的是同一段字，一条一行。
        Assert.Equal(string.Join(Environment.NewLine, "订单号", "A-1024"), answer.Copied);

        // 没有可列的读法时，引擎那句话还要有地方显示。
        Assert.True(answer.FallbackShown);
        Assert.Equal(Strings.Format("Look.Note", "没有找到参考图。"), answer.FallbackText);
    }

    /// <summary>
    /// 右侧的标记要能收窄、能一次全选：字多的画面里，找某一处读到的东西不该靠滚。
    /// </summary>
    [Fact]
    public void The_marks_can_be_filtered_and_all_of_them_ticked()
    {
        var answer = Ui.Run(() =>
        {
            var (window, _, _) = TextWindow();
            var rows = window.FindControl<StackPanel>("Marks")!.Children.OfType<Border>().ToList();

            var filter = window.FindControl<TextBox>("MarkFilter")!;
            filter.Text = "A-1024";
            Dispatcher.UIThread.RunJobs();
            var shown = rows.Count(row => row.IsVisible);

            filter.Text = string.Empty;
            Dispatcher.UIThread.RunJobs();
            var all = rows.Count(row => row.IsVisible);

            foreach (var toggle in rows
                         .SelectMany(row => row.GetVisualDescendants().OfType<CheckBox>())
                         .ToList())
            {
                toggle.IsChecked = false;
            }

            window.FindControl<Button>("SelectAllMarks")!
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var ticked = rows
                .SelectMany(row => row.GetVisualDescendants().OfType<CheckBox>())
                .Count(toggle => toggle.IsChecked == true);

            window.Close();
            return (Shown: shown, All: all, Ticked: ticked, Rows: rows.Count);
        });

        Assert.Equal(1, answer.Shown);
        Assert.Equal(answer.Rows, answer.All);
        Assert.Equal(answer.Rows, answer.Ticked);
    }
}
