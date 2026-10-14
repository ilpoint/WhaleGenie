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
using WhaleGenie.Execution;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Localization;
using WhaleGenie.Models;
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
            var window = new AddActionWindow(null, ActionCatalog.Definitions,
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

            // A condition is asked about rather than done, so there is nothing to run.
            viewModel.SelectAction("condition.imageExists");
            Dispatcher.UIThread.RunJobs();
            Assert.False(viewModel.ShowsRun);
            Assert.False(viewModel.CanRun);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>()
                    .Where(button => button.IsEffectivelyVisible),
                button => Equals(button.Content, Strings.Get("Add.RunStep")));
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
    /// 读到的字得能带走：右边每一行标记写出它读到的那一行，另有一个只读的框把这一次读到的字一行一条
    /// 列出来，可以选中，也可以按「复制」放进剪贴板。图上的字是给眼睛看的，带不走。
    /// </summary>
    [Fact]
    public void What_was_read_is_listed_and_can_be_copied()
    {
        var answer = Ui.Run(() =>
        {
            var (window, _, clipboard) = TextWindow();

            var written = window.FindControl<TextBox>("ReadText")!.Text ?? string.Empty;
            var rows = window.FindControl<StackPanel>("Marks")!
                .GetVisualDescendants().OfType<TextBlock>()
                .Select(text => text.Text ?? string.Empty)
                .ToList();

            window.FindControl<Button>("CopyText")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var copied = clipboard.Text;

            window.Close();
            return (Written: written, Rows: rows, Copied: copied);
        });

        // 引擎交出的 span 一条一行，顺序也一样。
        Assert.Equal(string.Join(Environment.NewLine, "订单号", "A-1024"), answer.Written);

        // 每一行标记把自己读到的字写出来。
        Assert.Contains("订单号", answer.Rows);
        Assert.Contains("A-1024", answer.Rows);

        // 「复制」放走的是同一段字。
        Assert.Equal(answer.Written, answer.Copied);
    }

    /// <summary>
    /// 图上的字默认不画：框里就是刚读到的那几行字，标签压在框里正好挡住要对着看的东西。要用带字的图给
    /// 人看时打开开关，字画在框的上沿外侧；上沿放不下就落到下沿外侧，始终不落进框里。
    /// </summary>
    [Fact]
    public void The_writing_is_only_drawn_over_the_picture_when_it_is_asked_for()
    {
        var answer = Ui.Run(() =>
        {
            var (window, _, _) = TextWindow();
            var board = window.FindControl<Canvas>("Board")!;
            var toggle = window.FindControl<CheckBox>("LabelsOnPicture")!;

            var before = Writing(board).Count;

            toggle.IsChecked = true;
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            var pairs = Pairs(board);
            var apart = pairs.All(pair => !pair.Text.Intersects(pair.Box)
                && (pair.Text.Bottom <= pair.Box.Top || pair.Text.Top >= pair.Box.Bottom));

            window.Close();
            return (Before: before, Drawn: pairs.Count, Apart: apart);
        });

        Assert.Equal(0, answer.Before);
        Assert.Equal(2, answer.Drawn);
        Assert.True(answer.Apart, "画在框外的字不该压进它自己那个框里");
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

    /// <summary>画面上那些写着字的控件。</summary>
    private static List<TextBlock> Writing(Canvas board)
        => [.. board.Children.OfType<TextBlock>().Where(text => text.Name == "MarkText")];

    /// <summary>画面上那些框，各按它被摆到哪儿量。</summary>
    private static List<(Rect Text, Rect Box)> Pairs(Canvas board)
        => [.. Writing(board).Select(text => (Where(text),
            Where(board.Children.OfType<Border>()
                .Single(box => Equals(box.Tag, text.Tag)))))];

    private static Rect Where(Control control)
        => new(Canvas.GetLeft(control), Canvas.GetTop(control), control.Bounds.Width,
            control.Bounds.Height);

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
    /// against the screen it came from and read again afterwards. A window filling the screen has no
    /// title bar to come back to, so it goes back to its normal size on the way out.
    /// </summary>
    [Fact]
    public void Putting_the_window_away_leaves_it_there_to_come_back_to()
    {
        Ui.Run(() =>
        {
            var window = new LookWindow(Found());
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Minimize(window);
            Assert.Equal(WindowState.Minimized, window.WindowState);

            // Nothing is lost: the same window comes back, still showing what it found.
            window.WindowState = WindowState.Normal;
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.IsVisible);

            // A window filling the screen is put back to its normal size on the way out, because
            // there is no title bar over it to click again.
            window.WindowState = WindowState.FullScreen;
            Dispatcher.UIThread.RunJobs();
            Minimize(window);
            Assert.Equal(WindowState.Minimized, window.WindowState);
        });
    }

    /// <summary>Clicks the button that puts the window away, the way a user finds it.</summary>
    private static void Minimize(Window window)
        => window.GetVisualDescendants().OfType<Button>()
            .Single(button => button.Name == "MinimizeButton")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

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
    /// window fills the screen and comes back, by the button and by F11, and Esc goes back to the
    /// window before it closes it.
    /// </summary>
    [Fact]
    public void The_window_fills_the_screen_and_comes_back()
    {
        Ui.Run(() =>
        {
            var window = new LookWindow(Found());
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var button = window.FindControl<Button>("FullScreen")!;
            Assert.Equal(Strings.Get("Look.FullScreen"), button.Content);

            window.KeyPressQwerty(PhysicalKey.F11, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(WindowState.FullScreen, window.WindowState);
            Assert.Equal(Strings.Get("Look.FullScreenExit"), button.Content);

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
    public void Fitting_shows_the_whole_picture_without_enlarging_a_small_one()
    {
        Assert.Equal(0.5, LookGeometry.Fit(1000, 500, 500, 250));
        Assert.Equal(1, LookGeometry.Fit(100, 50, 500, 250));
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
    /// The offset is only worth picking on an action that looks for a picture: a step that clicks
    /// writing has an offset too, but there is nothing to point at.
    /// </summary>
    [Theory]
    [InlineData("vision.clickImage", true)]
    [InlineData("ocr.clickText", false)]
    [InlineData("input.mouseClick", false)]
    public void Picking_a_click_point_is_offered_where_there_is_a_picture_to_point_at(
        string key, bool offered)
    {
        Ui.Run(() =>
        {
            var window = new AddActionWindow(null, ActionCatalog.Definitions,
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
