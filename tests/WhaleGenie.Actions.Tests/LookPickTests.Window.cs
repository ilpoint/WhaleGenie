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

// The entry window: one value a line, and how the list reads when it is empty.
public partial class LookPickTests
{

    /// <summary>一处最多几种颜色：多到没有意义时就说清楚，而不是一直往里加。</summary>
    [Fact]
    public void A_place_says_when_it_can_hold_no_more_colours()
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

            window.Pick(new ScreenPoint(5, 6));
            for (var number = 0; number < 12; number++)
            {
                window.AddAlternative(0, $"#{number:X6}");
            }

            Assert.Equal(ColourPointLine.MostColours, window.Added[0].Split('|').Length);
            window.Close();
        });
    }

    /// <summary>
    /// 编辑框一行一个值：偏移 X、偏移 Y、颜色各占一行。这里是手填一个点，三格写的就是数字和颜色，
    /// 所以一格都不带表达式按钮——算式仍然可以直接打进去，引擎照读。
    /// </summary>
    [Fact]
    public void The_entry_window_gives_every_value_a_line_of_its_own()
    {
        Ui.Run(() =>
        {
            var window = new PickEntryWindow("12,-4,#FF0000", [], false);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("12", window.FindControl<TextBox>("OffsetX")!.Text);
            Assert.Equal("-4", window.FindControl<TextBox>("OffsetY")!.Text);
            Assert.Equal("#FF0000", window.FindControl<TextBox>("Colour")!.Text);

            // 三格旁边没有表达式按钮：这一窗只填一个点，写算式的能力留在框里，不占一排空间。
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(),
                button => button.Tag is "OffsetX" or "OffsetY" or "Colour");
            window.Close();
        });
    }

    /// <summary>
    /// 三行各占一行、互不重叠：挤在一起时数值就分不清是哪一行的。
    /// </summary>
    [Fact]
    public void The_entry_window_rows_stand_one_above_the_next()
    {
        Ui.Run(() =>
        {
            var window = new PickEntryWindow("1,2,#FFFFFF", [], false);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var rows = new[] { "OffsetX", "OffsetY", "Colour" }
                .Select(name => window.FindControl<TextBox>(name)!)
                .Select(box => box.TranslatePoint(new Point(0, 0), window)!.Value)
                .ToList();

            for (var index = 1; index < rows.Count; index++)
            {
                Assert.True(rows[index].Y > rows[index - 1].Y + 24,
                    $"第 {index + 1} 行与上一行重叠：{rows[index - 1].Y:0.#} → {rows[index].Y:0.#}");
            }

            window.Close();
        });
    }

    /// <summary>
    /// 还没有一条时，那一栏也要看得出一张列表：标题、加号，和一句"尚未加入"；加进一条后那句话让位。
    /// </summary>
    [Fact]
    public void The_list_shows_what_it_is_before_anything_is_in_it()
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

            var entries = window.FindControl<StackPanel>("Entries")!;
            var hint = Strings.Get("LookPick.NothingAdded");

            Assert.True(window.FindControl<Border>("EntriesPanel")!.IsVisible);
            Assert.True(window.FindControl<Button>("AddEntry")!.IsVisible);
            Assert.Contains(entries.Children.OfType<TextBlock>(), text => text.Text == hint);

            window.AppendEntry("1,1,#FFFFFF");
            Assert.DoesNotContain(entries.Children.OfType<TextBlock>(), text => text.Text == hint);
            window.Close();
        });
    }

    private static string Readout(LookPickWindow window)
        => window.FindControl<TextBlock>("Readout")?.Text ?? string.Empty;
}
