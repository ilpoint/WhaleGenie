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

// A run in progress: the current row, the macro it names, and the buttons that steer it.
public partial class RunDebugTests
{
    /// <summary>
    /// 步骤列表跟着当前行走：正在跑的那一行滚进视野，上下各留一行余量；已经在视野里就不动。滚到哪儿
    /// 是纯函数算的（与 `LookGeometry` 同一路），所以这里喂进几种行号与视口高度，量的是"那一行整行
    /// 落在可视区里"，而不是"它滚过一下"。
    /// </summary>
    [Fact]
    public void The_list_scrolls_the_current_row_in_with_a_row_of_room_around_it()
    {
        // 一行 20 像素、视野 100 像素：一次看得见五行。
        const double row = 20;
        const double room = 100;

        // 已经在视野正中，不动；这一步是"列表不跟着跳"的那一半。
        Assert.Equal(240, RunScroll.Where(240, 300, row, room));

        // 在下面看不见：滚下来，让它下面留一行。
        Assert.Equal(240, RunScroll.Where(0, 300, row, room));

        // 在上面看不见：滚回去，让它上面留一行。
        Assert.Equal(80, RunScroll.Where(400, 100, row, room));

        // 视野比一行还矮：尽量让它进来，位置不拉到负数。
        Assert.Equal(510, RunScroll.Where(0, 500, row, 10));
        Assert.Equal(0, RunScroll.Where(-30, 0, row, room));

        // 从哪里滚过来都一样：停下时那一行整行都在视野里。
        foreach (var offset in new[] { 0d, 7, 60, 240, 900 })
        {
            foreach (var top in new[] { 0d, 19, 20, 300, 480, 900 })
            {
                var at = RunScroll.Where(offset, top, row, room);
                Assert.True(at >= 0, $"偏移不该是负的，得到 {at}");
                Assert.True(top - at >= 0 && top + row - at <= room,
                    $"行顶 {top} 在偏移 {at} 时越出视野");
            }
        }
    }
    /// <summary>
    /// 「跳过这一步」上了工具栏，与圆点右键那一份是同一件事：停住时可点，没停住时不可点。
    /// </summary>
    [Fact]
    public void Skip_this_step_is_only_clickable_while_the_run_is_paused()
    {
        var answer = Ui.RunAsync(async () =>
        {
            var held = Count("n", "1");
            var next = Count("after", "1");
            StepIds.Settle([held, next]);

            var window = new RunWindow([held, next], NullDeviceLayer.Instance);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (RunViewModel)window.DataContext!;
            var button = Find<Button>(window, "SkipStepButton");
            var wired = ReferenceEquals(button.Command, viewModel.SkipStepCommand);
            var before = button.Command!.CanExecute(null);

            viewModel.ToggleBreakpointCommand.Execute(viewModel.Steps.First(step => step.Id == held.Id));
            viewModel.RunCommand.Execute(null);
            var paused = await Until(() => viewModel.IsPaused);
            var during = button.Command!.CanExecute(null);

            button.Command.Execute(null);
            var done = await Until(() => !viewModel.IsBusy);
            var after = button.Command!.CanExecute(null);
            var label = viewModel.Steps.First(step => step.Id == held.Id).OutcomeLabel;

            window.Close();
            return (Wired: wired, Before: before, paused, during, done, After: after, Label: label,
                Wanted: Strings.Get("Run.OutcomeSkipped"));
        });

        Assert.True(answer.Wired, "工具栏那枚按钮接的该是同一个命令");
        Assert.False(answer.Before, "还没停住时不该能跳过");
        Assert.True(answer.paused, "断点该让运行停住");
        Assert.True(answer.during, "停住时该能跳过");
        Assert.True(answer.done, "跳过之后该接着跑完");
        Assert.False(answer.After, "跑完之后又该不能跳过");
        Assert.Equal(answer.Wanted, answer.Label);
    }

    /// <summary>
    /// 这扇窗接上三个快捷键，做的事与工具栏上那三枚按钮一模一样：F10 单步、F5 运行或继续、Shift+F5
    /// 停止。F5 一个键管两枚按钮，按下算哪一枚看运行停没停。
    /// </summary>
    [Fact]
    public void The_keys_do_what_the_run_buttons_do()
    {
        var answer = Ui.RunAsync(async () =>
        {
            var first = Count("n", "1");
            var second = Count("m", "1");
            StepIds.Settle([first, second]);

            var window = new RunWindow([first, second], NullDeviceLayer.Instance);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (RunViewModel)window.DataContext!;
            RunStepViewModel Row(string id) => viewModel.Steps.First(step => step.Id == id);

            // F10：先停在第一步之前，什么都还没做 —— 与「单步」按钮一样。
            window.KeyPressQwerty(PhysicalKey.F10, RawInputModifiers.None);
            var ready = await Until(() => viewModel.IsPaused);
            var nothing = !Row(first.Id).HasRun && !Row(second.Id).HasRun;

            // 再按一次：做一步，停在第二步之前。
            window.KeyPressQwerty(PhysicalKey.F10, RawInputModifiers.None);
            var oneStep = await Until(() => viewModel.IsPaused && Row(first.Id).HasRun);
            var later = Row(second.Id).HasRun;

            // F5：接着跑完，与「继续」按钮一样。
            window.KeyPressQwerty(PhysicalKey.F5, RawInputModifiers.None);
            var finished = await Until(() => !viewModel.IsBusy && Row(second.Id).HasRun);

            // Shift+F5：把正在跑的那一次停住，与「停止」按钮一样。
            var slow = SlowStep(5000);
            StepIds.Settle([slow]);
            var running = new RunWindow([slow], NullDeviceLayer.Instance);
            running.Show();
            Dispatcher.UIThread.RunJobs();

            var other = (RunViewModel)running.DataContext!;
            other.RunCommand.Execute(null);
            var going = await Until(() => other.IsBusy);
            running.KeyPressQwerty(PhysicalKey.F5, RawInputModifiers.Shift);
            var halted = await Until(() => !other.IsBusy);

            running.Close();
            window.Close();
            return (ready, nothing, oneStep, later, finished, going, halted);
        });

        Assert.True(answer.ready, "F10 该让运行停住");
        Assert.True(answer.nothing, "刚停住时什么都还没做");
        Assert.True(answer.oneStep, "再按一次 F10 该只做一步并停住");
        Assert.False(answer.later, "第二步不该在单步里跑掉");
        Assert.True(answer.finished, "F5 该从停下的地方接着跑完");
        Assert.True(answer.going, "那个慢宏该跑起来");
        Assert.True(answer.halted, "Shift+F5 该把它停住");
    }
}
