using System;
using System.IO;
using System.Linq;
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
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// The set-key button and the capture it arms: plain keys, the run button, and what closes it.
public partial class MacroEditorTests
{
    [Fact]
    public void Binding_enter_keeps_the_capture_closed()
    {
        Ui.Run(() =>
        {
            var (window, viewModel) = Open();
            viewModel.SetKeyCommand.Execute(null);

            Tap(window, PhysicalKey.Enter);

            Assert.Equal("Enter", viewModel.BindKey);
            Assert.False(viewModel.IsCapturingKey);
        });
    }

    /// <summary>
    /// 编辑器里起一次调试只有一条路：左侧「保存」下面那枚「运行调试」——把编辑器手里的步骤（不先
    /// 保存）装进运行与调试窗口并停在那儿。没有步骤时它按不动，有步骤之后按下去就该开出一扇装着这些
    /// 步骤的窗口，窗口自己不开跑。
    /// </summary>
    [Fact]
    public void The_editor_has_the_run_button_the_keyboard_has()
    {
        var answer = Ui.RunAsync(async () =>
        {
            // 空宏上那枚按钮按不动，写的宏才按得动。
            var (bare, _) = Open();
            var empty = RunButton(bare).IsEnabled;
            bare.Close();

            var macro = new MacroItem { Name = "probe" };
            macro.Steps.Add(Set("total", Formula.Number("7", 7d)));
            StepIds.Settle(macro.Steps);

            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (MacroEditorViewModel)window.DataContext!;
            var button = RunButton(window);
            var ready = button.IsEnabled;
            button.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();

            var running = window.OwnedWindows.OfType<RunWindow>().FirstOrDefault();
            var debugger = running is null ? null : (RunViewModel)running.DataContext!;
            var idle = debugger is { IsBusy: false }
                && debugger.Status == WhaleGenie.Localization.Strings.Get("Run.Ready");

            // 再按一次：旧的那扇关掉，只留一扇装着当前步骤的窗口，不该越按越多。
            button.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            var open = window.OwnedWindows.OfType<RunWindow>().Where(one => one.IsVisible).ToList();

            var answer = (Empty: empty, Ready: ready, Found: running is not null, Idle: idle,
                Ones: open.Count, Fresh: !ReferenceEquals(open[0], running),
                Wanted: viewModel.Steps.Select(step => step.Id).ToList(),
                Loaded: debugger?.Steps.Select(step => step.Id).ToList() ?? []);

            foreach (var one in open)
            {
                one.Close();
            }

            window.Close();
            return answer;
        });

        Assert.False(answer.Empty, "没有步骤时那枚按钮该按不动");
        Assert.True(answer.Ready, "写了步骤之后该能按");
        Assert.True(answer.Found, "按下去该开出一扇运行与调试窗口");
        Assert.True(answer.Idle, "步骤装进去之后该停着不跑");
        Assert.Equal(1, answer.Ones);
        Assert.True(answer.Fresh, "再按一次该换成一扇新的、只留一扇");
        Assert.NotEmpty(answer.Wanted);
        Assert.Equal(answer.Wanted, answer.Loaded);
    }

    /// <summary>The editor's own way into a run, found the way a person finds it: on the window.</summary>
    private static Button RunButton(Window window)
        => window.GetVisualDescendants()
            .OfType<Button>()
            .Single(found => found.Name == "RunDebugButton");

    /// <summary>Waits for something the run does on its own thread, giving up rather than hanging.</summary>
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
