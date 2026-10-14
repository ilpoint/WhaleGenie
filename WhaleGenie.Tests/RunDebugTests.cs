using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The run-and-debug window: it debugs one macro and manages the variables the project can use, and
/// which of the two it is showing follows from how it was opened. Opened from the button on its own
/// there is nothing to run, so the run controls are not drawn and the line that says where a debug
/// run starts from stands in their place.
/// </summary>
public class RunDebugTests
{
    [Fact]
    public void Opened_on_its_own_it_shows_the_variables_and_no_run_controls()
    {
        Ui.Run(() =>
        {
            var window = new RunWindow([]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (RunViewModel)window.DataContext!;

            Assert.True(viewModel.IsVariablesPage);
            Assert.False(viewModel.HasSteps);
            Assert.False(viewModel.CanStart);
            Assert.NotEmpty(viewModel.SystemVariables);

            Assert.False(Find<Border>(window, "RunToolbar").IsVisible);
            Assert.False(Find<Border>(window, "PageTabs").IsVisible);
            Assert.True(Find<TextBlock>(window, "DebugFromCardNote").IsVisible);

            window.Close();
        });
    }

    [Fact]
    public void Opened_on_a_macro_it_shows_the_steps_and_can_switch_to_the_variables()
    {
        Ui.Run(() =>
        {
            var window = new RunWindow(Steps("probe"));
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (RunViewModel)window.DataContext!;

            Assert.True(viewModel.IsDebugPage);
            Assert.True(viewModel.HasSteps);
            Assert.True(Find<Border>(window, "RunToolbar").IsVisible);
            Assert.False(Find<TextBlock>(window, "DebugFromCardNote").IsVisible);

            viewModel.SelectPageCommand.Execute("Variables");
            Dispatcher.UIThread.RunJobs();

            Assert.True(viewModel.IsVariablesPage);
            Assert.False(viewModel.IsDebugPage);

            viewModel.SelectPageCommand.Execute("Debug");
            Assert.True(viewModel.IsDebugPage);

            window.Close();
        });
    }

    /// <summary>
    /// The same window debugs the second macro: there is one of these at a time, and the run that
    /// was going is stopped to make room for the other macro's steps. Why it stopped is written into
    /// the log, because a run that ends early by itself looks like a failure.
    /// </summary>
    [Fact]
    public void Debugging_another_macro_stops_the_run_and_says_why()
    {
        Ui.RunAsync(async () =>
        {
            var first = new MacroItem { Name = "first" };
            first.Steps.Add(SlowStep(5000));

            var second = new MacroItem { Name = "second" };
            second.Steps.Add(SlowStep(5000));

            var viewModel = new RunViewModel([], NullDeviceLayer.Instance, 1, null, [first, second]);

            await viewModel.DebugAsync(first);
            Assert.True(viewModel.IsBusy);
            Assert.Equal("first", viewModel.Debugged);
            Assert.Equal("first", first.Name);
            Assert.True(viewModel.HasSteps);
            Assert.Contains("first", viewModel.WindowTitle, System.StringComparison.Ordinal);

            await viewModel.DebugAsync(second);
            Assert.Equal("second", viewModel.Debugged);

            // What the log opens with is the reason the run before it ended.
            Assert.Contains(viewModel.Lines,
                line => line.Text == WhaleGenie.Localization.Strings.Format(
                    "Run.DebugSwitched", "first", "second"));

            // Asking for the macro that is already running leaves it alone rather than starting it
            // again, which would throw away the run in progress.
            var going = viewModel.Lines.Count;
            await viewModel.DebugAsync(second);
            Assert.Equal(going, viewModel.Lines.Count);

            viewModel.StopCommand.Execute(null);
            while (viewModel.IsBusy)
            {
                await Task.Delay(10);
            }

            return true;
        });
    }

    /// <summary>A macro of one step that waits long enough for a check to catch it running.</summary>
    private static MacroStep SlowStep(int milliseconds) => new()
    {
        Type = "control.delay",
        Parameters =
        [
            new StepParameter { Name = "ms", Kind = ActionParameterKind.Number, Value = $"{milliseconds}" },
        ],
    };

    private static IEnumerable<MacroStep> Steps(string name)
    {
        var step = SlowStep(5);
        StepIds.Settle([step]);
        return [step];
    }

    /// <summary>
    /// What a step worked out is on its row whether or not the macro keeps it: ticking a result
    /// decides whether it becomes a variable of its own, and never whether it can be read while a
    /// run is being watched. It is what makes "tick it and run again" no longer the way to look.
    /// </summary>
    [Fact]
    public void What_a_step_worked_out_is_on_its_row_whether_or_not_the_macro_keeps_it()
    {
        var answer = Ui.RunAsync(async () =>
        {
            var untouched = Encode("hello");
            var kept = Encode("hello");
            kept.Parameters.Add(new StepParameter
            {
                Name = "returns",
                Kind = ActionParameterKind.Returns,
                Rows = [Row("encoded", "b64")],
            });
            StepIds.Settle([untouched, kept]);

            var viewModel = new RunViewModel([untouched, kept], NullDeviceLayer.Instance);
            viewModel.RunCommand.Execute(null);
            while (viewModel.IsBusy)
            {
                await Task.Delay(10);
            }

            var nobody = viewModel.Steps.First(step => step.Id == untouched.Id);
            var somebody = viewModel.Steps.First(step => step.Id == kept.Id);

            return (nobody.HasRun, nobody.OutcomeLabel, Count: nobody.Results.Count,
                Name: nobody.Results[0].Name, Value: nobody.Results[0].Value,
                Kept: nobody.Results[0].Kept, KeptName: somebody.Results[0].Name,
                KeptFlag: somebody.Results[0].Kept, somebody.ResultsHeader);
        });

        Assert.True(answer.HasRun);
        Assert.Equal(WhaleGenie.Localization.Strings.Get("Run.OutcomeOk"), answer.OutcomeLabel);
        Assert.Equal(1, answer.Count);

        // Nobody ticked this one, so it is shown under the engine's own name for it and marked as
        // one the macro does not keep.
        Assert.Equal("encoded", answer.Name);
        Assert.Equal("aGVsbG8=", answer.Value);
        Assert.False(answer.Kept);

        // The one the macro keeps reads as the name it goes by, and its header counts it.
        Assert.Equal("b64", answer.KeptName);
        Assert.True(answer.KeptFlag);
        Assert.Contains("1", answer.Item9, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// The panel is on the window itself, not only in the view model: what a step worked out is
    /// read off the screen when a run is watched, so a row that carried it in memory only would be
    /// no use to anybody.
    /// </summary>
    [Fact]
    public void The_row_shows_what_the_step_worked_out_on_the_window_itself()
    {
        var answer = Ui.RunAsync(async () =>
        {
            var step = Encode("hello");
            StepIds.Settle([step]);

            var window = new RunWindow([step], NullDeviceLayer.Instance);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (RunViewModel)window.DataContext!;
            viewModel.RunCommand.Execute(null);
            while (viewModel.IsBusy)
            {
                await Task.Delay(10);
            }

            Dispatcher.UIThread.RunJobs();

            var written = window.GetVisualDescendants().OfType<TextBlock>()
                .Where(text => text.IsEffectivelyVisible)
                .Select(text => text.Text ?? string.Empty)
                .ToList();

            window.Close();
            return written;
        });

        // The step, how it ended and what it worked out — under the engine's own name for it,
        // because nobody ticked it.
        Assert.Contains(answer, text => text.Contains("data.base64Encode", System.StringComparison.Ordinal));
        Assert.Contains(WhaleGenie.Localization.Strings.Get("Run.OutcomeOk"), answer);
        Assert.Contains("encoded", answer);
        Assert.Contains("aGVsbG8=", answer);
    }

    /// <summary>
    /// A breakpoint with a condition stops the run only while that condition holds; one whose
    /// condition is not met lets the run go past, which is the whole reason to write a condition.
    /// </summary>
    [Fact]
    public void A_breakpoint_stops_only_while_its_condition_holds()
    {
        var answer = Ui.RunAsync(async () =>
        {
            // A condition that is not met: the run goes past the breakpoint and finishes.
            var sets = Count("n", "1");
            var guarded = Count("m", "1");
            StepIds.Settle([sets, guarded]);
            var viewModel = new RunViewModel([sets, guarded], NullDeviceLayer.Instance);
            var row = viewModel.Steps.First(step => step.Id == guarded.Id);
            viewModel.ConditionBreakpoint(row, GreaterThan("$n", "3"));

            viewModel.RunCommand.Execute(null);
            var wentPast = await Until(() => !viewModel.IsBusy);
            var finished = viewModel.Status;
            var note = row.ConditionNote;
            var has = row.HasCondition;
            var ranAnyway = row.HasRun;

            // A condition that does hold: the run stops in front of the step and waits for the user.
            var setsAgain = Count("n", "1");
            var held = Count("m", "1");
            StepIds.Settle([setsAgain, held]);
            var second = new RunViewModel([setsAgain, held], NullDeviceLayer.Instance);
            var stopped = second.Steps.First(step => step.Id == held.Id);
            second.ConditionBreakpoint(stopped, GreaterThan("$n", "0"));

            second.RunCommand.Execute(null);
            var paused = await Until(() => second.IsPaused);
            var skipReady = second.SkipStepCommand.CanExecute(null);
            var notYet = stopped.HasRun;
            second.ContinueCommand.Execute(null);
            var done = await Until(() => !second.IsBusy);

            return (wentPast, finished, note, has, ranAnyway, paused, skipReady, done, notYet,
                Stopped: stopped.HasRun);
        });

        Assert.True(answer.wentPast, "the run never finished");
        Assert.NotEqual(WhaleGenie.Localization.Strings.Get("Run.Paused"), answer.finished);
        Assert.True(answer.ranAnyway, "the step the breakpoint was on never ran");
        Assert.True(answer.paused);
        Assert.True(answer.skipReady);
        Assert.False(answer.notYet, "the run went past the breakpoint it should have stopped at");
        Assert.True(answer.Stopped);
        Assert.True(answer.has);
        Assert.Contains("3", answer.note, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// A step a breakpoint stopped in front of can be left out: the run goes on to the next step,
    /// and this one is answered "not carried out" rather than being run quietly.
    /// </summary>
    [Fact]
    public void A_pause_can_leave_the_step_out_altogether()
    {
        var answer = Ui.RunAsync(async () =>
        {
            var held = Count("n", "1");
            var next = Count("after", "1");
            StepIds.Settle([held, next]);

            var viewModel = new RunViewModel([held, next], NullDeviceLayer.Instance);
            var row = viewModel.Steps.First(step => step.Id == held.Id);
            viewModel.ToggleBreakpointCommand.Execute(row);

            viewModel.RunCommand.Execute(null);
            var paused = await Until(() => viewModel.IsPaused);
            viewModel.SkipStepCommand.Execute(null);
            var done = await Until(() => !viewModel.IsBusy);

            return (paused, done, Ran: row.HasRun, Label: row.OutcomeLabel,
                Count: row.Results.Count,
                Next: viewModel.Steps.First(step => step.Id == next.Id).OutcomeLabel);
        });

        Assert.True(answer.paused, "the run never stopped at the breakpoint");
        Assert.True(answer.done, "the run never went on past the step it left out");
        Assert.True(answer.Ran);
        Assert.Equal(WhaleGenie.Localization.Strings.Get("Run.OutcomeSkipped"), answer.Label);
        Assert.Equal(0, answer.Count);
        Assert.Equal(WhaleGenie.Localization.Strings.Get("Run.OutcomeOk"), answer.Next);
    }

    /// <summary>
    /// The breakpoint's own menu, and the panel under a step's row. Both are what a debugger is
    /// read with, so both are checked where they are written rather than only through the view
    /// model: a menu item pointing at the wrong handler looks right until it is clicked.
    /// </summary>
    [Fact]
    public void The_breakpoint_offers_a_condition_a_clear_and_a_skip_and_the_row_shows_a_panel()
    {
        var markup = File.ReadAllText(
            Path.Combine(Repository(), "WhaleGenie", "Views", "RunWindow.axaml"));

        var menu = markup[markup.IndexOf("<Button.ContextMenu>", System.StringComparison.Ordinal)..];
        menu = menu[..menu.IndexOf("</Button.ContextMenu>", System.StringComparison.Ordinal)];

        Assert.Contains("Click=\"OnBreakpointMenuClicked\"", menu, System.StringComparison.Ordinal);
        Assert.Contains("Tag=\"condition\"", menu, System.StringComparison.Ordinal);
        Assert.Contains("Tag=\"clear\"", menu, System.StringComparison.Ordinal);
        Assert.Contains("Tag=\"skip\"", menu, System.StringComparison.Ordinal);
        Assert.Contains("[Run.BreakpointCondition]", menu, System.StringComparison.Ordinal);

        // The panel: what the step came to, whatever the macro keeps of it.
        Assert.Contains("IsVisible=\"{Binding HasRun}\"", markup, System.StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Results}\"", markup, System.StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding OutcomeLabel}\"", markup, System.StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding TimeNote}\"", markup, System.StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding FailureNote}\"", markup, System.StringComparison.Ordinal);
    }

    /// <summary>A step that works out a value on its own, with nothing but the engine behind it.</summary>
    private static MacroStep Encode(string text) => new()
    {
        Type = "data.base64Encode",
        Parameters = [new StepParameter { Name = "text", Kind = ActionParameterKind.Text, Value = text }],
    };

    /// <summary>A step that sets a variable, so a run has something to do that cannot fail.</summary>
    private static MacroStep Count(string name, string value) => new()
    {
        Type = "control.setVariable",
        Parameters =
        [
            new StepParameter { Name = "name", Kind = ActionParameterKind.Text, Value = name },
            new StepParameter { Name = "scope", Kind = ActionParameterKind.Text, Value = "local" },
            new StepParameter { Name = "value", Kind = ActionParameterKind.Text, Value = value },
        ],
    };

    /// <summary>A condition step asking whether a value is above another, as the form writes it.</summary>
    private static MacroStep GreaterThan(string variable, string value) => new()
    {
        Type = "condition.compare",
        Parameters =
        [
            new StepParameter { Name = "variable", Kind = ActionParameterKind.Text, Value = variable },
            new StepParameter
            {
                Name = "operator",
                Kind = ActionParameterKind.Choice,
                Value = "greaterThan",
            },
            new StepParameter { Name = "value", Kind = ActionParameterKind.Text, Value = value },
        ],
    };

    private static StepParameterRow Row(string key, string name) => new()
    {
        Columns = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["key"] = key,
            ["name"] = name,
        },
    };

    /// <summary>
    /// Waits for something a run does on its own thread, giving up rather than hanging: a run that
    /// never stops at a breakpoint would otherwise hold the whole check until the test times out.
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
    /// 开了「与别的宏互斥发输入」的宏，运行调试里看得见这一行 —— 两个宏同时跑时"现在是谁在发"是
    /// 唯一能解释"我的按键去哪儿了"的东西；没开这一格的宏不画它，因为答案永远是同一句话。
    /// </summary>
    [Fact]
    public void A_macro_that_takes_turns_says_who_is_sending_input()
    {
        Ui.Run(() =>
        {
            // 这个宏没有步骤，所以它跑不起来，这一行只是把窗口指到它上面。
            var macro = new MacroItem { Name = "取订单", ExclusiveInput = true };
            var window = new RunWindow([], null, 1, null, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (RunViewModel)window.DataContext!;
            Assert.False(viewModel.ShowsInputOwner);

            viewModel.DebugAsync(macro).GetAwaiter().GetResult();

            Assert.True(viewModel.ShowsInputOwner);
            Assert.Equal(Strings.Get("Run.InputOwnerNone"), viewModel.InputOwnerLine);

            window.Close();
        });
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
                Note: Find<TextBlock>(window, "DebugFromCardNote").IsVisible);

            viewModel.DebugAsync(macro).GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();

            var after = (Toolbar: Find<Border>(window, "RunToolbar").IsVisible,
                Note: Find<TextBlock>(window, "DebugFromCardNote").IsVisible);

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

            // 勾了「与别的宏互斥发输入」，那一行才画；速度调过，徽标才画。
            var macro = new MacroItem { Name = "取订单", ExclusiveInput = true };
            macro.Steps.Add(step);

            var window = new RunWindow([], NullDeviceLayer.Instance, 2, null, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (RunViewModel)window.DataContext!;
            viewModel.DebugAsync(macro).GetAwaiter().GetResult();
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
    /// A run answers with the name of the macro it is, so a step can write its own name into a log
    /// line or a file. The page that lists WhaleGenie's own variables shows that same answer rather
    /// than one read off the machine, which is what makes the wall worth looking at while a macro
    /// runs: it shows what a macro would read right now.
    /// </summary>
    [Fact]
    public void A_run_says_which_macro_it_is()
    {
        var answer = Ui.RunAsync(async () =>
        {
            var keeps = Count("留名", "$sys.macroName");
            StepIds.Settle([keeps]);

            var window = new RunWindow([keeps], NullDeviceLayer.Instance, macroName: "对账单");
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (RunViewModel)window.DataContext!;
            viewModel.RunCommand.Execute(null);
            while (viewModel.IsBusy)
            {
                await Task.Delay(10);
            }

            viewModel.RefreshCommand.Execute(null);

            var wall = viewModel.SystemVariables
                .First(row => row.Name == "sys.macroName").CurrentValue;
            var kept = viewModel.Variables.First(row => row.Name == "留名").Value;
            var title = viewModel.WindowTitle;

            window.Close();
            return (Wall: wall, Kept: kept, Title: title);
        });

        Assert.Equal("对账单", answer.Wall);
        Assert.Equal("对账单", answer.Kept);
        Assert.Contains("对账单", answer.Title, System.StringComparison.Ordinal);
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
