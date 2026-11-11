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
using WhaleGenie.Core.Expressions;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// The picks offered on the picture, and how far short one fell.
public partial class LookWindowTests
{
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
            new StepParameter { Name = "x", Kind = ActionParameterKind.Number, Written = Formula.FromLine("0") },
            new StepParameter { Name = "y", Kind = ActionParameterKind.Number, Written = Formula.FromLine("0") },
            new StepParameter { Name = "width", Kind = ActionParameterKind.Number, Written = Formula.FromLine("120") },
            new StepParameter { Name = "height", Kind = ActionParameterKind.Number, Written = Formula.FromLine("20") },
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
}
