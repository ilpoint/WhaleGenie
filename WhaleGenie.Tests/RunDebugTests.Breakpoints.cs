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

// The rows as they are drawn: notes, folds, loops, and what a breakpoint does to a run.
public partial class RunDebugTests
{
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
    /// Folding a group hides what is inside it and nothing else; opening it brings the steps back,
    /// however deep they sit.
    /// </summary>
    [Fact]
    public void Folding_a_group_hides_the_steps_inside_it()
    {
        Ui.Run(() =>
        {
            var inner = new MacroStep
            {
                Type = "control.delay",
                Parameters = [new StepParameter { Name = "ms", Kind = ActionParameterKind.Number, Written = Formula.Number(1) }],
            };
            var loop = new MacroStep
            {
                Type = "control.repeat",
                Parameters =
                [
                    new StepParameter { Name = "times", Kind = ActionParameterKind.Number, Written = Formula.Number(2) },
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
            Assert.True(child.IsShown);
            Assert.Equal("▾", holder.Marker);

            viewModel.ToggleFoldCommand.Execute(holder);
            Assert.False(holder.IsExpanded);
            Assert.False(child.IsShown);
            Assert.Equal("▸", holder.Marker);

            viewModel.ToggleFoldCommand.Execute(holder);
            Assert.True(child.IsShown);

            window.Close();
        });
    }

    /// <summary>
    /// The row names its fields the way the action's form does, and its bubble carries what the
    /// editor shows in full: what the action does and the note written under the step.
    /// </summary>
    [Fact]
    public void A_step_row_uses_the_forms_words_and_explains_itself_in_a_bubble()
    {
        Ui.Run(() =>
        {
            var set = Count("total", "7");
            set.Meta = new StepMeta { Comment = "记下这一轮的总数" };
            StepIds.Settle([set]);

            var window = new RunWindow([set]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var row = ((RunViewModel)window.DataContext!).Steps.First();
            Assert.Contains("变量名 = total", row.Detail, StringComparison.Ordinal);
            Assert.Contains("备注：记下这一轮的总数", row.Tooltip, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(row.Description));

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
                Parameters = [new StepParameter { Name = "ms", Kind = ActionParameterKind.Number, Written = Formula.Number(1) }],
            };
            var loop = new MacroStep
            {
                Type = "control.repeat",
                Parameters =
                [
                    new StepParameter { Name = "times", Kind = ActionParameterKind.Number, Written = Formula.Number(2) },
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
            // that is 22 wide and as tall as the row it marks.
            var guide = window.GetVisualDescendants().OfType<Border>()
                .First(border => border.DataContext is int);
            Assert.Equal(22, guide.Bounds.Width);
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
            Assert.Contains("变量名 = total", detail, StringComparison.Ordinal);
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
            new StepParameter { Name = "ms", Kind = ActionParameterKind.Number, Written = Fields.Shape($"{milliseconds}") },
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

        // The step, how it ended and what it worked out. The row is written in the reader's words,
        // the same ones the palette uses; the action's own key is in the row's bubble.
        Assert.Contains(answer, text => text.Contains("Base64 编码", System.StringComparison.Ordinal));
        Assert.Contains(WhaleGenie.Localization.Strings.Get("Run.OutcomeOk"), answer);
        Assert.Contains("encoded", answer);
        Assert.Contains("aGVsbG8=", answer);
    }
}
