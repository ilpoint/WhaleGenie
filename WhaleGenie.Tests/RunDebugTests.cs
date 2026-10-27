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
using WhaleGenie.Execution;
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
    /// <summary>
    /// A retried step is more than one attempt, and the debugger says so: each try's time and the
    /// budget left after it are what make a timeout and a retry readable after the run.
    /// </summary>
    [Fact]
    public void The_debugger_shows_each_try_and_what_was_left_of_the_budget()
    {
        var row = new RunStepViewModel
        {
            Id = "k3f9",
            Label = "Wait",
            Detail = string.Empty,
            Depth = 0,
            CanBreak = true,
        };

        row.Took(new StepRun
        {
            StepId = "k3f9",
            StepType = "control.delay",
            Outcome = "failed",
            ElapsedMs = 150,
            FailureKey = "Run.Timeout",
            FailureDetail = "300",
            TimeoutMs = 300,
            Attempts =
            [
                new StepAttempt
                {
                    Number = 1,
                    ElapsedMs = 100,
                    Outcome = "failed",
                    FailureKey = "Run.NoDevice",
                    RemainingMs = 200,
                },
                new StepAttempt
                {
                    Number = 2,
                    ElapsedMs = 50,
                    Outcome = "failed",
                    FailureKey = "Run.Timeout",
                    RemainingMs = 0,
                    Overran = true,
                },
            ],
        });

        Assert.True(row.HasAttempts);
        Assert.Equal(2, row.Attempts.Count);
        Assert.Equal(Strings.Format("Run.StepAttempts", 2), row.AttemptsHeader);
        Assert.True(row.Attempts[0].HasFailure);
        Assert.Contains("Run.NoDevice", row.Attempts[0].Failure, StringComparison.Ordinal);
        Assert.Contains(Strings.Get("Run.AttemptOverran"), row.Attempts[1].Failure,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_direct_run_asks_for_the_declared_parameters()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "有参数" };
            macro.Parameters.Add(new MacroParameter
            {
                Name = "数量",
                Required = true,
                Type = ActionReturnType.Number,
                Description = "要做多少次。",
            });
            macro.Parameters.Add(new MacroParameter
            {
                Name = "倍数",
                Required = false,
                Default = "3",
                Type = ActionReturnType.Number,
            });
            macro.Steps.Add(new MacroStep
            {
                Type = "control.delay",
                Meta = StepMeta.New,
                Parameters =
                [
                    new StepParameter
                    {
                        Name = "ms",
                        Kind = ActionParameterKind.Number,
                        Value = "0",
                    },
                ],
            });

            var window = new RunWindow(macro.Steps, callable: macro.Callable);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (RunViewModel)window.DataContext!;
            Assert.True(viewModel.HasArguments);
            Assert.Equal(["数量", "倍数"], viewModel.Arguments.Select(row => row.Name));
            Assert.True(window.FindControl<Border>("RunArgumentsPanel") is { IsVisible: true });

            var required = viewModel.Arguments[0];
            var optional = viewModel.Arguments[1];
            Assert.True(required.IsMissing);
            Assert.False(viewModel.CanStart);

            // The default belongs to the declaration: the row starts empty and says what it is.
            Assert.Equal(string.Empty, optional.Value);
            Assert.Contains("3", optional.Hint, StringComparison.Ordinal);
            Assert.False(optional.IsMissing);

            required.Value = "5";
            Dispatcher.UIThread.RunJobs();
            Assert.True(viewModel.CanStart);

            // A required field can still say "I mean an empty value".
            required.Value = string.Empty;
            required.Given = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(viewModel.CanStart);
        });
    }

    [Fact]
    public void Opened_on_its_own_it_shows_the_variables_and_no_run_controls()
    {
        Ui.Run(() =>
        {
            var window = new RunWindow([]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (RunViewModel)window.DataContext!;

            Assert.False(viewModel.HasSteps);
            Assert.False(viewModel.CanStart);
            Assert.Empty(viewModel.Variables);

            Assert.False(Find<Border>(window, "RunToolbar").IsVisible);
            Assert.True(Find<TextBlock>(window, "DebugFromCardNote").IsEffectivelyVisible);

            window.Close();
        });
    }

    [Fact]
    public void Opened_on_a_macro_it_shows_the_steps_and_the_watch_list()
    {
        Ui.Run(() =>
        {
            var window = new RunWindow(Steps("probe"));
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (RunViewModel)window.DataContext!;

            Assert.True(viewModel.HasSteps);

            // 装着步骤开着也不跑：谁想先看一眼、先下个断点，窗口不该已经动手了。
            Assert.False(viewModel.IsBusy);
            Assert.Equal(Strings.Get("Run.Ready"), viewModel.Status);
            Assert.True(viewModel.CanStart);
            Assert.True(viewModel.CanStep);

            Assert.True(Find<Border>(window, "RunToolbar").IsVisible);
            Assert.False(Find<TextBlock>(window, "DebugFromCardNote").IsEffectivelyVisible);

            window.Close();
        });
    }

    /// <summary>
    /// The note an action carries is written under the step, the same one the editor shows: it is
    /// where the person who wrote the macro says why the step is there.
    /// </summary>
    [Fact]
    public void A_step_row_carries_the_note_written_under_it()
    {
        Ui.Run(() =>
        {
            var set = Count("total", "7");
            set.Meta = new StepMeta { Comment = "记下这一轮的总数" };
            StepIds.Settle([set]);

            var window = new RunWindow([set]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (RunViewModel)window.DataContext!;
            var row = viewModel.Steps.First();
            Assert.Equal("记下这一轮的总数", row.Comment);
            Assert.True(row.HasComment);

            window.Close();
        });
    }

    /// <summary>
    /// The list is flat, so a step inside a loop is told how deep it sits and drawn under a guide
    /// line for each level; the step that holds it says so with a mark of its own.
    /// </summary>
    [Fact]
    public void A_step_inside_a_loop_is_drawn_under_it()
    {
        Ui.Run(() =>
        {
            var inner = new MacroStep
            {
                Type = "control.delay",
                Parameters = [new StepParameter { Name = "ms", Kind = ActionParameterKind.Number, Value = "1" }],
            };
            var loop = new MacroStep
            {
                Type = "control.repeat",
                Parameters =
                [
                    new StepParameter { Name = "times", Kind = ActionParameterKind.Number, Value = "2" },
                    new StepParameter { Name = "body", Kind = ActionParameterKind.Steps, Steps = [inner] },
                ],
            };
            StepIds.Settle([loop, inner]);

            var window = new RunWindow([loop]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (RunViewModel)window.DataContext!;
            var holder = viewModel.Steps[0];
            var child = viewModel.Steps[1];

            Assert.True(holder.IsContainer);
            Assert.Equal("▾", holder.Marker);
            Assert.Empty(holder.Guides);

            Assert.False(child.IsContainer);
            Assert.Equal(1, child.Depth);
            Assert.Single(child.Guides);

            // The guide is drawn, not only described: a row inside the loop carries one line cell
            // that is 14 wide and as tall as the row it marks.
            var guide = window.GetVisualDescendants().OfType<Border>()
                .First(border => border.DataContext is int);
            Assert.Equal(14, guide.Bounds.Width);
            Assert.True(guide.Bounds.Height > 0);

            window.Close();
        });
    }

    /// <summary>
    /// The watch list is about the macro, not about how far the run has got: every variable the
    /// macro creates or reads is listed from the moment it is loaded, with its type, and the value
    /// stays empty until something writes it.
    /// </summary>
    [Fact]
    public void The_watch_list_names_the_variables_before_the_run_reaches_them()
    {
        Ui.RunAsync<bool>(async () =>
        {
            var set = Count("total", "7");
            var other = Count("other", "1");
            StepIds.Settle([set, other]);

            var window = new RunWindow([set, other]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (RunViewModel)window.DataContext!;

            // Before the run reaches it: the row is there, with nothing in it yet.
            var before = viewModel.Variables.First(variable => variable.Name == "total");
            Assert.Equal(string.Empty, before.Value);
            Assert.Equal(Strings.Get("Variable.Scope.Local"), before.Scope);

            viewModel.RunCommand.Execute(null);
            var done = await Until(() => !viewModel.IsBusy);
            Dispatcher.UIThread.RunJobs();

            // After the run wrote it: the same row, with the value and the type it ended up holding.
            var after = viewModel.Variables.First(variable => variable.Name == "total");
            Assert.Equal("7", after.Value);
            Assert.Equal(Strings.Get("Variable.Type.number"), after.LocalType);
            window.Close();
            return done;
        });
    }

    /// <summary>Every step row carries the step's own name, which is what a breakpoint and a log line call it.</summary>
    [Fact]
    public void A_step_row_says_which_step_it_is()
    {
        Ui.Run(() =>
        {
            var set = Count("total", "7");
            StepIds.Settle([set]);

            var detail = RunViewModel.Detail(new[] { set }.ToExecutable()[0]);

            Assert.StartsWith(set.Id, detail, StringComparison.Ordinal);
            Assert.Contains("control.setVariable", detail, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// The two halves and the two halves of the right column are dragged against each other, and
    /// each says how small it may get so neither can be dragged out of sight.
    /// </summary>
    [Fact]
    public void The_debug_half_has_splitters_that_keep_both_sides_visible()
    {
        Ui.Run(() =>
        {
            var window = new RunWindow(Steps("probe"));
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var side = Find<GridSplitter>(window, "RunSplitter");
            var inside = Find<GridSplitter>(window, "WatchSplitter");
            Assert.Equal(GridResizeDirection.Columns, side.ResizeDirection);
            Assert.Equal(GridResizeDirection.Rows, inside.ResizeDirection);

            // The minimum belongs to the column the splitter sizes, not to the panel inside it: a
            // MinWidth on the panel was never consulted, which is how the steps column was made
            // narrow enough to lose its text.
            var columns = Assert.IsType<Grid>(side.Parent).ColumnDefinitions;
            Assert.True(columns[0].MinWidth >= 200);
            Assert.True(columns[2].MinWidth >= 200);

            window.Close();
        });
    }

    /// <summary>
    /// The same window takes the second macro: there is one of these at a time, and the run that was
    /// going is stopped to make room for the other macro's steps. Why it stopped is written into the
    /// log of the next run, because a run that ends early by itself looks like a failure. Loading a
    /// macro does not itself start anything, so the second macro is loaded and stands there until
    /// 「运行」 is pressed.
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
            Assert.Equal("first", viewModel.Debugged);
            Assert.Equal("first", first.Name);
            Assert.True(viewModel.HasSteps);
            Assert.Contains("first", viewModel.WindowTitle, System.StringComparison.Ordinal);

            // 装载不再自己跑起来：装完是停着的，跑要按「运行」。
            Assert.False(viewModel.IsBusy);
            Assert.True(viewModel.CanStart);
            Assert.Equal(Strings.Get("Run.Ready"), viewModel.Status);

            viewModel.RunCommand.Execute(null);
            Assert.True(viewModel.IsBusy);

            await viewModel.DebugAsync(second);
            Assert.Equal("second", viewModel.Debugged);
            Assert.False(viewModel.IsBusy);

            // 换成第二个宏之后停着，跑起来时日志开头写的是上一个宏为什么停了。
            viewModel.RunCommand.Execute(null);
            Assert.True(viewModel.IsBusy);

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
        Parameters = [new StepParameter { Name = "dataText", Kind = ActionParameterKind.Text, Value = text }],
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
        Type = "control.if",
        Parameters =
        [
            new StepParameter
            {
                Name = "condition",
                Kind = ActionParameterKind.Expression,
                Value = $"{variable} > {value}",
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
            Path.Combine(Repository(), "WhaleGenie", "Views", "RunWindow.axaml"));

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
