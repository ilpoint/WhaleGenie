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

/// <summary>
/// Where the marks of a look land on the picture it came from. A mark is in screen pixels and the
/// picture has a corner of its own, so the window has to subtract one and scale by the zoom — and
/// a hit found by a colour is one pixel of a whole screen, which has to stay visible anyway.
/// </summary>
public class LookWindowTests
{
    /// <summary>
    /// The dialog carries one button that runs the step being written, so a step can be tried
    /// without closing the macro and going into a run. It is there for the steps a run can do — a
    /// condition is not a step of the macro, so there is nothing to run — and it waits until the
    /// step is complete, because half a step is not the step the user means yet.
    /// </summary>
    [Fact]
    public void The_dialog_runs_the_step_it_is_editing()
    {
        Ui.Run(() =>
        {
            var window = new ActionEditorWindow(null, ActionCatalog.Definitions,
                VariableChoicesForChecks.Named("match"), []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            Assert.False(viewModel.ShowsRun);

            viewModel.SelectAction("input.keyPress");
            Dispatcher.UIThread.RunJobs();
            Assert.True(viewModel.ShowsRun);

            // The key is still empty, which is a step that would send nothing.
            Assert.False(viewModel.CanRun);
            Assert.False(FindRunButton(window).IsEnabled);

            viewModel.Parameters.First(parameter => parameter.Definition.Name == "key").Text = "F5";
            Dispatcher.UIThread.RunJobs();
            Assert.True(viewModel.CanRun);
            Assert.True(FindRunButton(window).IsEnabled);
            Assert.Equal(Strings.Get("Add.RunStep"), FindRunButton(window).Content);

            // Blocks are runnable steps now; their condition is edited inside the same form.
            viewModel.SelectAction("control.if");
            Dispatcher.UIThread.RunJobs();
            Assert.True(viewModel.ShowsRun);
        });
    }

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

    /// <summary>
    /// 每一条候选写出它离门槛还差多少：顶上那句"最高 0.62，需要 0.90"是整张画面的答案，落不到某一条
    /// 候选上；没写门槛的步骤（例如抓图）没有可差的，就不写这一行。
    /// </summary>
    [Fact]
    public void A_candidate_says_how_far_short_it_fell()
    {
        var answer = Ui.Run(() =>
        {
            var look = Found() with
            {
                Minimum = 0.9,
                Boxes =
                [
                    new LookBox(new ImageMatch(0.62, new ScreenPoint(4, 3), new ScreenSize(6, 4)),
                        LookRole.Candidate),
                    new LookBox(new ImageMatch(0.95, new ScreenPoint(9, 6), new ScreenSize(6, 4)),
                        LookRole.Hit),
                ],
                ChosenIndex = 2,
            };

            var window = new LookWindow(look);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var rows = window.FindControl<StackPanel>("Marks")!
                .GetVisualDescendants().OfType<TextBlock>()
                .Select(text => text.Text ?? string.Empty)
                .ToList();

            var without = new LookWindow(Found());
            without.Show();
            Dispatcher.UIThread.RunJobs();
            var plain = without.FindControl<StackPanel>("Marks")!
                .GetVisualDescendants().OfType<TextBlock>()
                .Select(text => text.Text ?? string.Empty)
                .ToList();

            window.Close();
            without.Close();
            return (Rows: rows, Plain: plain, Short: Strings.Format("Look.Short", "28%"));
        });

        // 差的那一条写着差多少，够的那一条不写。
        Assert.Contains(answer.Short, answer.Rows);
        Assert.Single(answer.Rows, text => text.Contains(answer.Short, StringComparison.Ordinal));

        // 没写门槛的步骤不写这一行。
        Assert.DoesNotContain(answer.Plain,
            text => text.Contains(answer.Short, StringComparison.Ordinal));
    }

    /// <summary>
    /// 一扇从"刚写下的这一步"开的识别结果窗口：一台会读两行字、并记住被问过几次的机器，与一个记账的
    /// 剪贴板，都装进一台除了它们什么都不做的机器里。
    /// </summary>
    private static (LookWindow Window, ReadingForChecks Reading, ClipboardForChecks Clipboard) TextWindow()
    {
        var reading = new ReadingForChecks("订单号", "A-1024");
        var clipboard = new ClipboardForChecks();
        var devices = new ChecksDevices
        {
            Screen = new ScreenForChecks(),
            Ocr = reading,
            Clipboard = clipboard,
        };

        var step = new[] { Recognize() }.ToExecutable()[0];
        var look = MacroRunner.LookOnce(step, devices).Look!;
        var window = new LookWindow(look, step, devices);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, reading, clipboard);
    }

    /// <summary>读屏幕左上角一块的识别步骤，写给上面那台会读字的机器用。</summary>
    private static MacroStep Recognize() => new()
    {
        Type = "ocr.recognize",
        Parameters =
        [
            new StepParameter { Name = "x", Kind = ActionParameterKind.Number, Value = "0" },
            new StepParameter { Name = "y", Kind = ActionParameterKind.Number, Value = "0" },
            new StepParameter { Name = "width", Kind = ActionParameterKind.Number, Value = "120" },
            new StepParameter { Name = "height", Kind = ActionParameterKind.Number, Value = "20" },
        ],
    };

    /// <summary>
    /// 一台会读字的机器：每一次读都交出同样那几行，并记住被问过几次 —— "再看一次"到底看没看，看的就是
    /// 这个数。
    /// </summary>
    private sealed class ReadingForChecks(params string[] words) : IOcrDevice
    {
        /// <summary>How many times the screen has been read.</summary>
        public int Looked { get; private set; }

        public IReadOnlyList<TextSpan> Recognize(ImageFrame frame)
        {
            Looked++;
            return
            [
                .. words.Select((word, index) => new TextSpan(word, new ScreenPoint(index * 40, 2),
                    new ScreenSize(36, 8), 0.9)),
            ];
        }
    }

    /// <summary>A screen with one plain picture on it, which is all a reading of writing needs.</summary>
    private sealed class ScreenForChecks : IScreenDevice
    {
        public ScreenLayout Desktop { get; } = new(new ScreenArea(0, 0, 120, 40),
            [new ScreenDisplay(new ScreenArea(0, 0, 120, 40), true)]);

        public ScreenPoint Cursor => default;

        public PixelColor PixelAt(int x, int y) => default;

        public ImageFrame Capture(int x, int y, int width, int height)
            => new(width, height, new byte[width * height * 4]);

        public ScreenShot Capture(ScreenCaptureRequest request)
            => new(new ImageFrame(request.Width, request.Height,
                new byte[request.Width * request.Height * 4]), new ScreenPoint(request.X, request.Y),
                CaptureWay.Gdi);

        public ImageFrame? Load(string path) => new(4, 4, new byte[64]);
    }

    /// <summary>记账的剪贴板：谁往里写过什么，这是唯一看得到的东西。</summary>
    private sealed class ClipboardForChecks : IClipboardDevice
    {
        public string Text { get; private set; } = string.Empty;

        public int ChangeCount => 0;

        public bool HasText => Text.Length > 0;

        public string ReadText() => Text;

        public ImageFrame? ReadImage() => null;

        public void WriteText(string text) => Text = text;

        public void WriteImage(ImageFrame image)
        {
        }

        public IReadOnlyList<string> ReadFiles() => [];

        public void WriteFiles(IReadOnlyList<string> paths)
        {
        }

        public void Clear() => Text = string.Empty;
    }

    /// <summary>
    /// Waits for something a second look does on a pool thread, giving up rather than hanging.
    /// </summary>
    private static async Task<bool> Until(Func<bool> ready, int milliseconds = 5000)
    {
        for (var waited = 0; waited < milliseconds; waited += 10)
        {
            if (ready())
            {
                return true;
            }

            await Task.Delay(10);
        }

        return ready();
    }

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

    /// <summary>
    /// A picture is read at its own size, and a screenful of hits does not fit in a window: the
    /// window fills the screen and comes back by F11, and Esc goes back to the window before it
    /// closes it.
    /// </summary>
    /// <remarks>
    /// There is no button of its own for this any more: the title bar's maximize is that button now,
    /// and having both would be two controls for one window state.
    /// </remarks>
    [Fact]
    public void The_window_fills_the_screen_and_comes_back()
    {
        Ui.Run(() =>
        {
            var window = new LookWindow(Found());
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Null(window.FindControl<Button>("FullScreen"));

            window.KeyPressQwerty(PhysicalKey.F11, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(WindowState.FullScreen, window.WindowState);

            // Esc is the way back before it is the way out: the full screen is what just happened.
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(WindowState.Normal, window.WindowState);
            Assert.True(window.IsVisible);

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.IsVisible);
        });
    }

    [Fact]
    public void The_zoom_is_applied_to_the_mark_as_well_as_to_the_picture()
    {
        var place = LookGeometry.Place(
            new ScreenPoint(0, 0), new ScreenPoint(10, 10), new ScreenSize(4, 4), 2.5);

        Assert.Equal(new LookRect(25, 25, 10, 10), place);
    }

    [Fact]
    public void A_one_pixel_hit_is_enlarged_so_that_it_can_be_seen_and_stays_where_it_was()
    {
        var visible = LookGeometry.Visible(new LookRect(40, 60, 1, 1), 9);

        Assert.Equal(9, visible.Width);
        Assert.Equal(9, visible.Height);

        // The middle is still the middle: the mark grew around the pixel, not off to one side.
        Assert.Equal(40.5, visible.X + (visible.Width / 2));
        Assert.Equal(60.5, visible.Y + (visible.Height / 2));
    }

    [Fact]
    public void A_mark_bigger_than_the_smallest_one_is_left_as_it_is()
    {
        var visible = LookGeometry.Visible(new LookRect(5, 6, 20, 10), 9);

        Assert.Equal(new LookRect(5, 6, 20, 10), visible);
    }

    [Fact]
    public void Fitting_shows_the_whole_picture_and_enlarges_a_small_one()
    {
        Assert.Equal(0.5, LookGeometry.Fit(1000, 500, 500, 250));

        // 参考图只有几十像素宽：留在 100% 就是窗口正中一枚邮票，适应窗口要把它放大。
        Assert.Equal(5, LookGeometry.Fit(100, 50, 500, 250));
        Assert.Equal(LookGeometry.LargestZoom, LookGeometry.Fit(10, 10, 5000, 5000));

        // 没有画面时没有可适应的东西，留在原大。
        Assert.Equal(1, LookGeometry.Fit(0, 0, 500, 250));
    }

    [Fact]
    public void Zooming_in_and_out_stays_inside_what_the_view_allows()
    {
        Assert.True(LookGeometry.Step(1, true) > 1);
        Assert.True(LookGeometry.Step(1, false) < 1);
        Assert.Equal(LookGeometry.LargestZoom, LookGeometry.Step(LookGeometry.LargestZoom, true));
        Assert.Equal(LookGeometry.SmallestZoom, LookGeometry.Step(LookGeometry.SmallestZoom, false));
    }

    /// <summary>
    /// The click point a step uses is measured from the middle of the picture: that is where a
    /// click lands when the step asks for no offset, and where a picked point has to be counted
    /// from for the numbers to mean what the field says.
    /// </summary>
    [Theory]
    [InlineData(40, 20, 35, 5, 15, -5)]
    [InlineData(40, 20, 20, 10, 0, 0)]
    [InlineData(41, 21, 0, 0, -20, -10)]
    public void A_point_on_the_picture_becomes_an_offset_from_its_middle(
        int width, int height, int x, int y, int offsetX, int offsetY)
    {
        var offset = LookGeometry.Offset(width, height, new ScreenPoint(x, y));

        Assert.Equal(new ScreenPoint(offsetX, offsetY), offset);
    }

    [Fact]
    public void Clicking_outside_the_picture_is_a_click_at_its_edge()
    {
        // Drawn four times its size, a click on the far corner is still inside the picture.
        Assert.Equal(new ScreenPoint(9, 4), LookGeometry.Inside(10, 5, 4, 38, 18));
        Assert.Equal(new ScreenPoint(9, 4), LookGeometry.Inside(10, 5, 4, 400, 400));
        Assert.Equal(new ScreenPoint(0, 0), LookGeometry.Inside(10, 5, 4, -50, -50));
    }

    /// <summary>
    /// 偏移点只在"这一步刚好看了一眼屏幕"时才有得拾取：命中的中心是偏移的原点，而那一眼看到的画面
    /// 就是可点的底图。找图、识别文字、识别图像都在此列；点一下鼠标这类动作有偏移的写法，却没有可点的
    /// 东西，不给按钮。
    /// </summary>
    [Theory]
    [InlineData("vision.clickImage", true)]
    [InlineData("ocr.clickText", true)]
    [InlineData("vision.clickDetect", true)]
    [InlineData("input.mouseClick", false)]
    public void Picking_a_click_point_is_offered_where_the_step_looked_at_the_screen(
        string key, bool offered)
    {
        Ui.Run(() =>
        {
            var window = new ActionEditorWindow(null, ActionCatalog.Definitions,
                VariableChoicesForChecks.Named("match"), []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.SelectAction(key);
            Dispatcher.UIThread.RunJobs();

            var pickers = viewModel.Rows.Concat(viewModel.AdvancedRows)
                .Where(row => row.ShowsOffsetPick)
                .ToList();

            Assert.Equal(offered ? 1 : 0, pickers.Count);
            if (offered)
            {
                // The two halves of the offset sit on the one line the button is under.
                Assert.Equal("offsetX", pickers[0].First.Definition.Name);
                Assert.Equal("offsetY", pickers[0].Second?.Definition.Name);
            }
        });
    }
}
