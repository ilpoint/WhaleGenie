using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// Taking what a run worked out away, and the dot that says what clicking it does.
public partial class RunDebugTests
{
    /// <summary>
    /// 运行调试里算出来的东西要能带走：每一步的结果值与盯着的变量值都写在能选中的控件里，日志那一栏与每一行
    /// 变量各有一枚「复制」，按下去放进的是这台机器自己的剪贴板。同一件事在「这一步看到了什么」那扇窗里已经
    /// 做成了，运行调试里不能只有眼睛用得上。
    /// </summary>
    [Fact]
    public void What_the_run_worked_out_can_be_carried_away()
    {
        var answer = Ui.RunAsync(async () =>
        {
            var clipboard = new RecordingClipboard();
            var devices = new ChecksDevices { Clipboard = clipboard };

            var set = Count("total", "7");
            var encoded = Encode("hello");
            StepIds.Settle([set, encoded]);

            var macro = new MacroItem { Name = "probe" };
            macro.Steps.Add(set);
            macro.Steps.Add(encoded);

            var window = new RunWindow([set, encoded], devices, listed: [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (RunViewModel)window.DataContext!;
            viewModel.RunCommand.Execute(null);
            var done = await Until(() => !viewModel.IsBusy);
            Dispatcher.UIThread.RunJobs();

            // 值本身写在能选中的控件里。
            var shown = window.GetVisualDescendants().OfType<SelectableTextBlock>()
                .Select(block => block.Text ?? string.Empty)
                .ToList();

            // 日志那一栏：一枚按钮把整份日志（时刻带文字）放进剪贴板。
            Find<Button>(window, "CopyLog").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var log = clipboard.Text;

            // 盯着的变量：每一行也有自己那一枚，按下去的是那一行的值。
            ClickCopy(window, button => button.DataContext is WatchedVariable { Name: "total" });
            var watched = clipboard.Text;

            var lines = string.Join(Environment.NewLine,
                viewModel.Lines.Select(line => $"{line.Time}  {line.Text}"));
            var row = viewModel.Variables.First(variable => variable.Name == "total");

            window.Close();
            return (Done: done, Shown: shown, Log: log, Lines: lines, Watched: watched,
                FromPage: watched, RowValue: row.Value);
        });

        Assert.True(answer.Done, "这个宏该跑完");
        Assert.Contains("aGVsbG8=", answer.Shown);
        Assert.Contains("7", answer.Shown);
        Assert.Equal(answer.Lines, answer.Log);
        Assert.Equal("7", answer.Watched);
        Assert.Equal(answer.RowValue, answer.FromPage);
    }

    /// <summary>按下一行里那枚「复制」，按的是行自己那一枚而不是随便哪一枚。</summary>
    private static void ClickCopy(Window window, Func<Button, bool> pick)
        => window.GetVisualDescendants()
            .OfType<Button>()
            .First(pick)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    /// <summary>
    /// 步骤行左边那枚圆点是唯一说"能在这里下断点"的地方，它的提示就得说清点一下会发生什么 ——
    /// 原来写的是这一步的名字，点下去会怎样只能靠猜。
    /// </summary>
    [Fact]
    public void The_dot_says_what_clicking_it_does()
    {
        var markup = File.ReadAllText(
            ViewSource.PathOf("RunWindow.axaml"));

        var dot = markup[markup.IndexOf("<Button Classes=\"Breakpoint\"", StringComparison.Ordinal)..];
        dot = dot[..dot.IndexOf('>', StringComparison.Ordinal)];

        Assert.Contains("[Run.BreakpointDot]", dot, StringComparison.Ordinal);
        Assert.DoesNotContain("Binding Label", dot, StringComparison.Ordinal);

        // 提示里写着"下断点"这件事，两种语言都是。
        Assert.Contains("breakpoint", Strings.English["Run.BreakpointDot"],
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("断点", Strings.Chinese["Run.BreakpointDot"], StringComparison.Ordinal);
    }

    /// <summary>The repository root, found by walking up from the test binaries.</summary>
    private static string Repository()
    {
        for (var at = new DirectoryInfo(System.AppContext.BaseDirectory); at is not null; at = at.Parent)
        {
            if (File.Exists(Path.Combine(at.FullName, "WhaleGenie.slnx")))
            {
                return at.FullName;
            }
        }

        throw new System.InvalidOperationException("WhaleGenie.slnx was not found above the test binaries.");
    }

    private static T Find<T>(Window window, string name)
        where T : Control
        => window.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);
}
