using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The editor's "set key" button, driven with real key events. The release of the key that was
/// just bound has to be swallowed, or a focused button reads Space as a click and arms the
/// capture all over again.
/// </summary>
public class MacroEditorTests
{
    private static (MacroEditorWindow Window, MacroEditorViewModel ViewModel) Open()
    {
        var macro = new MacroItem { Name = "probe" };
        var window = new MacroEditorWindow(macro, [macro]);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, (MacroEditorViewModel)window.DataContext!);
    }

    private static void Tap(MacroEditorWindow window, PhysicalKey key)
    {
        window.KeyPressQwerty(key, RawInputModifiers.None);
        window.KeyReleaseQwerty(key, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static MacroStep Set(string name, string value)
        => new()
        {
            Type = "control.setVariable",
            Meta = StepMeta.New,
            Parameters =
            [
                new StepParameter { Name = "name", Kind = ActionParameterKind.Text, Value = name },
                new StepParameter { Name = "scope", Kind = ActionParameterKind.Choice, Value = "local" },
                new StepParameter { Name = "value", Kind = ActionParameterKind.Expression, Value = value },
            ],
        };

    /// <summary>
    /// Lifting takes the selected steps out of the macro and leaves a call to a new macro in their
    /// place. The values the stretch read become that macro's arguments and the values it wrote
    /// become its results, so the steps around the call go on reading the same names — and the call
    /// is written the way the dialog would have written it, rather than as a second magic shape.
    /// </summary>
    [Fact]
    public void A_stretch_of_steps_can_be_lifted_into_a_macro_of_its_own()
    {
        Ui.Run(() =>
        {
            var (window, viewModel) = Open();

            var started = Set("开始", "0");
            var counted = Set("中间", "$开始 + 1");
            var noted = Set("标记", "$中间");
            var after = Set("之后", "$中间");
            viewModel.Steps.Add(started);
            viewModel.Steps.Add(counted);
            viewModel.Steps.Add(noted);
            viewModel.Steps.Add(after);
            viewModel.SetSelection([counted, noted]);

            MacroItem? lifted = null;
            viewModel.MacroLifted += macro => lifted = macro;
            viewModel.LiftSelectedCommand.Execute(null);

            Assert.NotNull(lifted);
            Assert.Equal([counted, noted], lifted!.Steps);
            Assert.Equal(["开始"], lifted.Parameters.Select(parameter => parameter.Name));
            Assert.True(lifted.Parameters[0].Required);
            // The names the stretch writes are offered back in a settled order, so the same stretch
            // lifted twice writes the same call.
            Assert.Equal(["中间", "标记"], lifted.Results.Select(result => result.Name));

            // The stretch is out of the macro and a call stands where it was.
            Assert.Equal(3, viewModel.Steps.Count);
            Assert.Same(started, viewModel.Steps[0]);
            var call = viewModel.Steps[1];
            Assert.Equal("control.runMacro", call.Type);
            Assert.Equal(lifted.Name, call.Text("macro"));
            Assert.Equal("$开始", call.Text("arg.开始"));
            Assert.Same(after, viewModel.Steps[2]);

            var wanted = call.Parameters.First(parameter => parameter.Name == "returns").Rows;
            Assert.Equal(["中间", "标记"], wanted.Select(row => row.Text("key")));
            Assert.Equal(["中间", "标记"], wanted.Select(row => row.Text("name")));

            // The call is one the project can reach, so the step is not marked as calling something
            // that is not there; and the name it brings back is what the step after it reads.
            Assert.Empty(viewModel.Problems.Select(problem => problem.Message));
            Assert.Contains(lifted, window.LiftedMacros);

            // A lift that is undone leaves a macro nothing calls, and a macro nothing calls has no
            // business joining the project.
            viewModel.Steps.Remove(call);
            Assert.Empty(window.LiftedMacros);
        });
    }

    [Fact]
    public void The_macro_the_editor_ends_with_is_read_from_the_window()
    {
        Ui.Run(() =>
        {
            var (window, viewModel) = Open();
            Assert.Null(window.Result);

            viewModel.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            // The list is not waiting on the editor — it steps aside instead — so what the editor
            // ends with has to be readable from the window as it closes.
            Assert.NotNull(window.Result);
            Assert.Equal("probe", window.Result!.Name);

            var letGo = new MacroEditorWindow(new MacroItem { Name = "other" }, []);
            letGo.Show();
            Dispatcher.UIThread.RunJobs();
            letGo.Close(null);
            Dispatcher.UIThread.RunJobs();

            Assert.Null(letGo.Result);
        });
    }

    [Fact]
    public void The_editor_is_not_a_child_of_the_window_it_was_opened_from()
    {
        Ui.Run(() =>
        {
            var list = new Window();
            list.Show();
            Dispatcher.UIThread.RunJobs();

            var (window, _) = Open();
            window.ShowAsPeer(list);
            Dispatcher.UIThread.RunJobs();

            // Not owned, so the list going out of the way does not take the editor with it; it
            // still matches the list's pin, so a pinned list does not end up over it.
            Assert.Empty(list.OwnedWindows);
            Assert.True(window.IsVisible);

            list.Topmost = true;
            var pinned = new Window();
            pinned.ShowAsPeer(list);
            Assert.True(pinned.Topmost);
        });
    }

    [Fact]
    public void Binding_the_space_key_does_not_arm_the_capture_again()
    {
        Ui.Run(() =>
        {
            var (window, viewModel) = Open();
            viewModel.SetKeyCommand.Execute(null);

            Tap(window, PhysicalKey.Space);

            Assert.Equal("Space", viewModel.BindKey);
            Assert.False(viewModel.IsCapturingKey);
        });
    }

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

    [Theory]
    [InlineData(PhysicalKey.ShiftRight, "右Shift")]
    [InlineData(PhysicalKey.ShiftLeft, "左Shift")]
    [InlineData(PhysicalKey.ControlRight, "右Ctrl")]
    [InlineData(PhysicalKey.NumPad7, "NumPad7")]
    public void A_binding_says_which_key_it_saw(PhysicalKey key, string expected)
    {
        Ui.Run(() =>
        {
            var (window, viewModel) = Open();
            viewModel.SetKeyCommand.Execute(null);

            Tap(window, key);

            Assert.Equal(expected, viewModel.BindKey);
            Assert.Equal(expected, viewModel.BindKeyDisplay);
        });
    }

    [Fact]
    public void The_palette_keeps_one_copy_button()
    {
        Ui.Run(() =>
        {
            var (_, viewModel) = Open();

            // Duplicating and copying were the same idea twice; only Ctrl + C is left.
            Assert.Single(viewModel.ActionPalette, entry => entry.Key == "copy");
            Assert.DoesNotContain(viewModel.ActionPalette, entry => entry.Key == "duplicate");
        });
    }

    [Fact]
    public void Dropping_a_step_further_down_reorders_the_list()
    {
        Ui.Run(() =>
        {
            var (_, viewModel) = Open();
            var first = new MacroStep { Type = "control.delay" };
            var second = new MacroStep { Type = "control.delay" };
            viewModel.AddStep(first);
            viewModel.AddStep(second);

            viewModel.SetSelection([first]);
            viewModel.MoveSelectionTo(2);

            Assert.Equal(2, viewModel.Steps.Count);
            Assert.Same(second, viewModel.Steps[0]);
            Assert.Same(first, viewModel.Steps[1]);

            // The dragged step stays picked, so it can be moved again without another click.
            Assert.Same(first, Assert.Single(viewModel.SelectedSteps));
        });
    }

    [Fact]
    public void Dropping_a_step_where_it_already_was_changes_nothing()
    {
        Ui.Run(() =>
        {
            var (_, viewModel) = Open();
            var first = new MacroStep { Type = "control.delay" };
            var second = new MacroStep { Type = "control.log" };
            viewModel.AddStep(first);
            viewModel.AddStep(second);

            viewModel.SetSelection([second]);
            viewModel.MoveSelectionTo(1);

            Assert.Equal(["control.delay", "control.log"],
                viewModel.Steps.Select(step => step.Type));

            // A drag that lands where it started must not fill the undo history. One undo
            // therefore reaches past it, to the step that was added before the drag. What is
            // read back is the kind of step rather than the object itself: the history keeps
            // its own copies, so undoing hands the list a fresh step with the same contents.
            viewModel.UndoCommand.Execute(null);
            Assert.Equal("control.delay", Assert.Single(viewModel.Steps).Type);
        });
    }

    [Fact]
    public void A_mouse_drag_carries_a_step_past_the_one_below_it()
    {
        Ui.Run(() =>
        {
            var (window, viewModel) = Open();
            var first = new MacroStep { Type = "control.delay" };
            var second = new MacroStep { Type = "control.delay" };
            viewModel.AddStep(first);
            viewModel.AddStep(second);
            Dispatcher.UIThread.RunJobs();

            var rows = window.GetVisualDescendants().OfType<ListBoxItem>().ToList();
            Assert.Equal(2, rows.Count);

            // Grab the first row by its middle and let go past the middle of the second,
            // which is where a drop "after it" is asked for.
            var from = Waypoint(window, rows[0], 0.5, 0.5);
            var to = Waypoint(window, rows[1], 0.5, 0.9);

            // The pressed button has to travel with the move: a drag is only a drag while the
            // pointer reports the button still down.
            window.MouseDown(from, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseMove(to, RawInputModifiers.LeftMouseButton);
            window.MouseUp(to, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Same(second, viewModel.Steps[0]);
            Assert.Same(first, viewModel.Steps[1]);
        });
    }

    /// <summary>A point inside a row, as a fraction of its width and height, in window space.</summary>
    private static Point Waypoint(Window window, Visual row, double x, double y)
        => row.TranslatePoint(new Point(row.Bounds.Width * x, row.Bounds.Height * y), window) ?? default;

    [Fact]
    public void A_break_outside_a_loop_is_marked_on_its_row()
    {
        Ui.Run(() =>
        {
            var (_, viewModel) = Open();
            var loose = new MacroStep { Type = "control.break" };
            viewModel.AddStep(loose);

            Assert.True(viewModel.HasProblems);
            Assert.Same(loose, Assert.Single(viewModel.Problems).Step);
            Assert.True(Assert.Single(viewModel.Rows, row => row.IsStep).HasProblem);
        });
    }

    [Fact]
    public void A_break_inside_a_loop_leaves_the_list_quiet()
    {
        Ui.Run(() =>
        {
            var (_, viewModel) = Open();
            viewModel.AddStep(new MacroStep
            {
                Type = "control.repeat",
                Parameters =
                [
                    new StepParameter { Name = "times", Kind = ActionParameterKind.Number, Value = "3" },
                    new StepParameter
                    {
                        Name = "body",
                        Kind = ActionParameterKind.Steps,
                        Steps = [new MacroStep { Type = "control.break" }],
                    },
                ],
            });

            Assert.False(viewModel.HasProblems);
            Assert.All(viewModel.Rows.Where(row => row.IsStep), row => Assert.False(row.HasProblem));
        });
    }

    [Fact]
    public void Fixing_a_problem_clears_the_mark_again()
    {
        Ui.Run(() =>
        {
            var (_, viewModel) = Open();
            var loose = new MacroStep { Type = "control.continue" };
            viewModel.AddStep(loose);
            Assert.True(viewModel.HasProblems);

            // Undoing the step that was complained about takes the mark with it.
            viewModel.UndoCommand.Execute(null);

            Assert.False(viewModel.HasProblems);
        });
    }

    [Fact]
    public void The_warning_mark_points_at_the_step_the_check_flagged()
    {
        Ui.Run(() =>
        {
            var (window, viewModel) = Open();
            viewModel.AddStep(new MacroStep { Type = "control.break" });
            Dispatcher.UIThread.RunJobs();

            var mark = Assert.Single(Marks(window));
            Assert.True(mark.IsVisible);
            Assert.Contains("break", ToolTip.GetTip(mark) as string ?? string.Empty);

            // The line above the list says there is something to look at.
            var summary = Assert.Single(window.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Classes.Contains("ProblemSummary"));
            Assert.True(summary.IsVisible);
            Assert.False(string.IsNullOrWhiteSpace(summary.Text));
        });
    }

    [Fact]
    public void A_step_with_nothing_wrong_carries_no_mark()
    {
        Ui.Run(() =>
        {
            var (window, viewModel) = Open();
            viewModel.AddStep(new MacroStep { Type = "control.delay" });
            Dispatcher.UIThread.RunJobs();

            Assert.False(Assert.Single(Marks(window)).IsVisible);
        });
    }

    /// <summary>The warning mark every row carries, whether or not it is showing.</summary>
    private static List<Avalonia.Controls.Shapes.Path> Marks(Window window)
        => [.. window.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>()
            .Where(path => path.Classes.Contains("ProblemMark"))];

    /// <summary>
    /// 「与别的宏互斥发输入」写进宏文件：换台机器打开、或者从包里读回来，这个开关还在 —— 它是这个
    /// 宏自己的性质，不是一个只在这次会话里管用的界面状态。
    /// </summary>
    [Fact]
    public void Taking_turns_sending_input_is_kept_in_the_macro()
    {
        Ui.Run(() =>
        {
            var (window, viewModel) = Open();
            viewModel.ExclusiveInput = true;

            viewModel.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            var saved = window.Result;
            Assert.NotNull(saved);
            Assert.True(saved!.ExclusiveInput);
            Assert.True(saved.ToJson()["exclusiveInput"]!.GetValue<bool>());

            // 读回来也还在，而且默认是关的：一个宏单独跑时不该为谁停下来。
            Assert.True(MacroItem.FromJson(saved.ToJson()).ExclusiveInput);
            Assert.False(new MacroItem { Name = "other" }.ExclusiveInput);
        });
    }
}
