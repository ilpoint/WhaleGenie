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
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// What a step worked out, the pause that skips it, and the input line while the door is held.
public partial class RunDebugTests
{
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
            viewModel.SetBreakpointRule(row, new BreakpointRule { Expression = Formula.Binary(">", Formula.Name("n"), Formula.Number(3)) });

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
            second.SetBreakpointRule(stopped, new BreakpointRule { Expression = Formula.Binary(">", Formula.Name("n"), Formula.Number(0)) });

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
        Assert.Contains("「n」 > 3", answer.note, System.StringComparison.Ordinal);
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
    /// The breakpoint's menu — the one a step's row carries — and the panel under a step's row. Both
    /// are what a debugger is read with, so both are checked where they are written rather than only
    /// through the view model: a menu item pointing at the wrong handler looks right until it is
    /// clicked.
    /// </summary>
    [Fact]
    public void The_row_menu_offers_the_breakpoint_and_the_row_shows_a_panel()
    {
        var markup = File.ReadAllText(
            Path.Combine(Repository(), "WhaleGenie", "Views", "RunWindow.axaml"));

        var menu = markup[markup.IndexOf("<Border.ContextMenu>", System.StringComparison.Ordinal)..];
        menu = menu[..menu.IndexOf("</Border.ContextMenu>", System.StringComparison.Ordinal)];

        Assert.Contains("Click=\"OnBreakpointMenuClicked\"", menu, System.StringComparison.Ordinal);
        Assert.Contains("Tag=\"toggle\"", menu, System.StringComparison.Ordinal);
        Assert.Contains("Tag=\"condition\"", menu, System.StringComparison.Ordinal);
        Assert.Contains("Tag=\"clear\"", menu, System.StringComparison.Ordinal);
        Assert.Contains("[Run.BreakpointSet]", menu, System.StringComparison.Ordinal);
        Assert.Contains("[Run.BreakpointOff]", menu, System.StringComparison.Ordinal);
        Assert.Contains("[Run.BreakpointCondition]", menu, System.StringComparison.Ordinal);
        Assert.DoesNotContain("Tag=\"skip\"", menu, System.StringComparison.Ordinal);
        Assert.DoesNotContain("[Run.RunTo]", markup, System.StringComparison.Ordinal);

        // 圆点自己不再挂菜单：它只有 16 像素宽，右键它沿控件树落到这一行上。
        Assert.DoesNotContain("<Button.ContextMenu>", markup, System.StringComparison.Ordinal);

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
        Parameters = [new StepParameter { Name = "dataText", Kind = ActionParameterKind.Text, Written = Formula.FromLine(text) }],
    };

    /// <summary>A step that sets a variable, so a run has something to do that cannot fail.</summary>
    private static MacroStep Count(string name, string value) => new()
    {
        Type = "control.setVariable",
        Parameters =
        [
            new StepParameter { Name = "name", Kind = ActionParameterKind.Text, Written = Formula.FromLine(name) },
            new StepParameter { Name = "scope", Kind = ActionParameterKind.Text, Written = Formula.FromLine("local") },
            new StepParameter { Name = "value", Kind = ActionParameterKind.Text, Written = Formula.FromLine(value) },
        ],
    };

    /// <summary>A condition step asking whether a value is above another, as the form writes it.</summary>
    private static MacroStep GreaterThan(string variable, string value) => new()
    {
        Type = "control.if",
        Parameters =
        [
            new StepParameter
            {
                Name = "condition",
                Kind = ActionParameterKind.Expression,
                Written = Formula.FromLine($"{variable} > {value}"),
            },
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
    /// 有人占着输入门时，运行调试里看得见这一行 —— 两个宏同时跑时"现在是谁在发"是唯一能解释
    /// "我的按键去哪儿了"的东西；没有人在门里时不画它，因为那时候没有话可说。
    /// </summary>
    [Fact]
    public void The_input_line_appears_while_someone_holds_the_door()
    {
        Ui.Run(() =>
        {
            var window = new RunWindow([], null, 1);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (RunViewModel)window.DataContext!;
            Assert.False(viewModel.ShowsInputOwner);

            var held = InputGate.Take("别的宏", default).GetAwaiter().GetResult();
            try
            {
                viewModel.RefreshInputOwner();
                Assert.True(viewModel.ShowsInputOwner);
                Assert.Equal(Strings.Format("Run.InputOwner", "别的宏"), viewModel.InputOwnerLine);
            }
            finally
            {
                held.Dispose();
            }

            viewModel.RefreshInputOwner();
            Assert.False(viewModel.ShowsInputOwner);

            window.Close();
        });
    }
}
