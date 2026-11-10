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

// A window with nothing to run: the offer it makes, and what loading a macro draws.
public partial class RunDebugTests
{
    /// <summary>
    /// 从主界面标题栏开的那扇窗是空手开的，它得给出"从哪儿开始"的入口：一个列出项目里所有宏的下拉，
    /// 加一枚「开始调试」。装上宏之后这两样让位给运行控制，与从卡片开的那扇一样。
    /// </summary>
    [Fact]
    public void An_empty_window_offers_a_macro_to_debug_and_gives_way_to_the_run_controls()
    {
        var answer = Ui.Run(() =>
        {
            var armed = new MacroItem { Name = "对账单" };
            var unarmed = new MacroItem { Name = "另存的", IsEnabled = false };
            armed.Steps.Add(SlowStep(5));
            unarmed.Steps.Add(SlowStep(5));

            var window = new RunWindow([], NullDeviceLayer.Instance, listed: [armed, unarmed]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var picker = Find<ComboBox>(window, "MacroPicker");
            var start = Find<Button>(window, "StartDebugButton");
            var empty = (
                Shown: picker.IsEffectivelyVisible && start.IsEffectivelyVisible,
                Choices: picker.ItemsSource!.Cast<MacroItem>().Select(macro => macro.Name).ToList(),
                Ready: start.Command!.CanExecute(null),
                Toolbar: Find<Border>(window, "RunToolbar").IsVisible);

            var viewModel = (RunViewModel)window.DataContext!;
            viewModel.ChosenMacro = armed;
            Dispatcher.UIThread.RunJobs();
            var ready = start.Command!.CanExecute(null);

            viewModel.DebugAsync(armed).GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();

            var after = (
                Picker: picker.IsEffectivelyVisible,
                Start: start.IsEffectivelyVisible,
                Toolbar: Find<Border>(window, "RunToolbar").IsVisible);

            window.Close();
            return (Empty: empty, Ready: ready, After: after);
        });

        // 空手开着：选宏与开始调试都在，宏按主列表的顺序列出来 —— 没启用的也列，那是触发器上的开关，
        // 不是能不能跑。
        Assert.True(answer.Empty.Shown, "空手开着的窗口该给出选宏与开始调试");
        Assert.Equal(["对账单", "另存的"], answer.Empty.Choices);
        Assert.False(answer.Empty.Ready, "还没选宏时不该能开始");
        Assert.False(answer.Empty.Toolbar);

        // 选了宏就能开始；装上之后这两样让位给运行控制。
        Assert.True(answer.Ready, "选了宏之后该能开始");
        Assert.False(answer.After.Picker, "装上宏之后选宏该让位");
        Assert.False(answer.After.Start);
        Assert.True(answer.After.Toolbar, "装上宏之后运行控制该出现");
    }

    /// <summary>
    /// 下拉里站着哪个宏，「载入这个宏」按下就装上哪个：与卡片右键走的是同一条路。装完停着，
    /// 要跑得在窗口里按「运行」。
    /// </summary>
    [Fact]
    public void The_picker_loads_the_macro_it_is_standing_on()
    {
        var answer = Ui.RunAsync(async () =>
        {
            var first = new MacroItem { Name = "first" };
            first.Steps.Add(Count("a", "1"));
            var second = new MacroItem { Name = "second" };
            second.Steps.Add(Count("b", "1"));
            second.Steps.Add(Count("c", "1"));
            StepIds.Settle([.. first.Steps, .. second.Steps]);

            var window = new RunWindow([], NullDeviceLayer.Instance, listed: [first, second]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (RunViewModel)window.DataContext!;
            Find<ComboBox>(window, "MacroPicker").SelectedItem = second;
            Dispatcher.UIThread.RunJobs();

            Find<Button>(window, "StartDebugButton").Command!.Execute(null);

            // 装载就是装载：步骤进来了，一行都还没跑过，窗口也不忙。
            var loaded = viewModel.Steps.Select(step => step.Id).ToList();
            var idle = (Busy: viewModel.IsBusy, Ran: viewModel.Steps.Any(step => step.HasRun),
                Status: viewModel.Status, CanStart: viewModel.CanStart, CanStep: viewModel.CanStep);

            viewModel.RunCommand.Execute(null);

            // 等它真的跑过：刚按下时那扇窗还没开始跑，"不在忙"那一刻什么都还没发生。
            var done = await Until(() =>
                viewModel.Steps.Count > 0 && viewModel.Steps.All(step => step.HasRun));
            var ran = viewModel.Steps.All(step => step.HasRun);
            var ids = viewModel.Steps.Select(step => step.Id).ToList();
            var answer = (Done: done, Ran: ran, Ids: ids, Debugged: viewModel.Debugged,
                Loaded: loaded, Idle: idle, Wanted: second.Steps.Select(step => step.Id).ToList());

            window.Close();
            return answer;
        });

        Assert.False(answer.Idle.Busy, "装进去之后窗口该停着不跑");
        Assert.False(answer.Idle.Ran, "装进去之后一行都还没跑过");
        Assert.Equal(Strings.Get("Run.Ready"), answer.Idle.Status);
        Assert.True(answer.Idle.CanStart, "装进去之后该能按「运行」");
        Assert.True(answer.Idle.CanStep, "装进去之后该能单步");
        Assert.Equal(answer.Wanted, answer.Loaded);

        Assert.True(answer.Done, "按下「运行」之后那扇窗该跑起来");
        Assert.Equal("second", answer.Debugged);
        Assert.Equal(answer.Wanted, answer.Ids);
        Assert.True(answer.Ran, "两个步骤都该跑过");
    }

    /// <summary>
    /// 从主界面开的那扇窗是空手开的，宏装进去之后工具栏与「单步」这枚按钮才该画出来 —— 卡片上的
    /// 「运行调试」走的正是这条路：先开一扇空的，再把宏装进去。照"没有步骤"画出来的东西得有人告诉它
    /// 改口，不然装上宏之后窗口里还是没有"开始"的地方。
    /// </summary>
    [Fact]
    public void Loading_a_macro_into_an_empty_window_draws_the_run_controls()
    {
        var answer = Ui.Run(() =>
        {
            var step = SlowStep(5);
            StepIds.Settle([step]);
            var macro = new MacroItem { Name = "对账单" };
            macro.Steps.Add(step);

            var window = new RunWindow([], NullDeviceLayer.Instance, listed: [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (RunViewModel)window.DataContext!;
            var before = (Toolbar: Find<Border>(window, "RunToolbar").IsVisible,
                Note: Find<TextBlock>(window, "DebugFromCardNote").IsEffectivelyVisible);

            viewModel.DebugAsync(macro).GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();

            var after = (Toolbar: Find<Border>(window, "RunToolbar").IsVisible,
                Note: Find<TextBlock>(window, "DebugFromCardNote").IsEffectivelyVisible);

            window.Close();
            return (Before: before, After: after);
        });

        Assert.False(answer.Before.Toolbar);
        Assert.True(answer.Before.Note, "空手开着的那扇窗本来该写着从卡片开始调试");

        Assert.True(answer.After.Toolbar, "装上宏之后工具栏该出现");
        Assert.False(answer.After.Note);
    }

    /// <summary>
    /// 工具栏那一行按「按钮 / 状态 / 倍速 / 谁在发输入」排开：倍速徽标与"现在是谁在发输入"各占自己
    /// 那格，两个都画出来时不该叠在一起。Avalonia 把超出列数的东西全归到最后一列，原来那两行因此落
    /// 在同一个位置上 —— 所以这里量的是无头会话里真实的矩形，而不是数一数列号。
    /// </summary>
    [Fact]
    public void The_speed_badge_and_the_input_line_are_not_drawn_on_each_other()
    {
        var answer = Ui.Run(() =>
        {
            var step = SlowStep(5);
            StepIds.Settle([step]);

            // 有人占着门，那一行才画；速度调过，徽标才画。
            var macro = new MacroItem { Name = "取订单" };
            macro.Steps.Add(step);

            var window = new RunWindow([], NullDeviceLayer.Instance, 2, null, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (RunViewModel)window.DataContext!;
            viewModel.DebugAsync(macro).GetAwaiter().GetResult();

            using var held = InputGate.Take("别的宏", default).GetAwaiter().GetResult();
            viewModel.RefreshInputOwner();
            Dispatcher.UIThread.RunJobs();

            var badge = Find<Border>(window, "SpeedBadge");
            var owner = Find<TextBlock>(window, "InputOwnerText");
            var answer = (
                Both: badge.IsEffectivelyVisible && owner.IsEffectivelyVisible,
                Badge: new Rect(badge.TranslatePoint(default, window) ?? default, badge.Bounds.Size),
                Owner: new Rect(owner.TranslatePoint(default, window) ?? default,
                    owner.Bounds.Size));

            window.Close();
            return answer;
        });

        // 两个都画出来才量得出它们是不是叠在一起，所以先确认这一点，否则这一条什么都没看。
        Assert.True(answer.Both, "倍速徽标与输入那一行都该画出来");
        Assert.True(answer.Badge.Width > 0 && answer.Owner.Width > 0, "两个都该有自己的宽度");
        Assert.False(answer.Badge.Intersects(answer.Owner),
            $"倍速徽标 {answer.Badge} 与输入那一行 {answer.Owner} 叠在一起了");
    }
}
