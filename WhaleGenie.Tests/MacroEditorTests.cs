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
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The editor's "set key" button, driven with real key events: one key or a combination of one.
/// The release of the key that was just bound has to be swallowed, or a focused button reads Space
/// as a click and arms the capture all over again.
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
            // What the call passes is one list of rows: the lifted macro's argument, and the value
            // the stretch already had under that name.
            var passed = call.Parameters.First(parameter => parameter.Name == FieldNames.Arguments).Rows;
            Assert.Equal(["开始"], passed.Select(row => row.Text(FieldNames.Name)));
            Assert.Equal(["$开始"], passed.Select(row => row.Text(FieldNames.Value)));
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

    /// <summary>
    /// A lifted macro takes the values the stretch read, and each of them keeps the kind it already
    /// had. Calling them all text would make the call site say a number is text, which is a
    /// disagreement the author would have to find by reading the declaration.
    /// </summary>
    [Fact]
    public void A_lifted_macro_declares_the_kind_its_arguments_already_had()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "主" };
            macro.Parameters.Add(new MacroParameter
            {
                Name = "数",
                Type = ActionReturnType.Number,
                Required = true,
            });

            var counted = Set("中间", "$数 + 1");
            macro.Steps.Add(counted);

            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (MacroEditorViewModel)window.DataContext!;
            MacroItem? lifted = null;
            viewModel.MacroLifted += made => lifted = made;
            viewModel.SetSelection([counted]);
            viewModel.LiftSelectedCommand.Execute(null);

            Assert.NotNull(lifted);
            var argument = Assert.Single(lifted!.Parameters);
            Assert.Equal("数", argument.Name);
            Assert.Equal(ActionReturnType.Number, argument.Type);
            Assert.True(argument.Required);

            // Nothing said what the written name holds — a plain assignment is text until something
            // declares otherwise — so it is declared text rather than guessed at.
            var result = Assert.Single(lifted.Results);
            Assert.Equal("中间", result.Name);
            Assert.Equal(ActionReturnType.Text, result.Type);
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
            macro.Steps.Add(Set("total", "7"));
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

    /// <summary>
    /// A controller button can be the binding. Nothing tells the editor about one — Windows has no
    /// event for it — so the editor reads the controller while it is waiting, and the first button
    /// it finds going down is the binding, exactly as the first key is.
    /// </summary>
    [Fact]
    public void A_controller_button_can_be_taken_as_the_binding()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };

            // Held before the waiting starts, so what happens to a button that was already down can
            // be seen as well.
            var pad = new FakeGamepad { Down = GamepadButtons.A };
            var window = new MacroEditorWindow(macro, [macro], null,
                new ChecksDevices { GamepadReading = pad });
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (MacroEditorViewModel)window.DataContext!;
            viewModel.SetKeyCommand.Execute(null);
            Assert.True(viewModel.IsCapturingKey, "the editor is not waiting for a binding");

            // A button that was already down when the waiting started is not a press: it has to come
            // up and go down again, the way a key somebody is resting a finger on has to.
            window.LookAtController();
            Assert.True(viewModel.IsCapturingKey, "a button that was already down finished the capture");

            // Nor is a button nobody is touching.
            pad.Down = GamepadButtons.None;
            window.LookAtController();
            Assert.True(viewModel.IsCapturingKey);

            pad.Down = GamepadButtons.LeftBumper;
            window.LookAtController();

            Assert.Equal("Pad LB", viewModel.BindKey);
            Assert.Equal("Pad LB", viewModel.BindKeyDisplay);
            Assert.False(viewModel.IsCapturingKey, "the capture stayed open after the button");

            // And a button pressed after the capture is over binds nothing again.
            pad.Down = GamepadButtons.B;
            window.LookAtController();
            Assert.Equal("Pad LB", viewModel.BindKey);
        });
    }

    /// <summary>
    /// Every way the engine can compare a window has a translated summary, so a captured rule never
    /// puts a resource key on screen.
    /// </summary>
    [Fact]
    public void The_window_ways_are_offered_in_the_order_the_engine_reads_them()
    {
        Ui.Run(() =>
        {
            var (_, viewModel) = Open();

            foreach (var way in WindowHow.Values)
            {
                viewModel.UseWindowTarget(new WindowRule(Title: "Notepad", How: way));
                Assert.DoesNotContain("WindowRule.", viewModel.WindowTargetSummary,
                    StringComparison.Ordinal);
            }
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
    public void A_binding_takes_the_modifiers_that_are_down_when_its_last_key_arrives()
    {
        Ui.Run(() =>
        {
            var (window, viewModel) = Open();
            viewModel.SetKeyCommand.Execute(null);

            // Ctrl and Shift go down and stay down: pressing a modifier does not finish the capture,
            // because it may be the front of a combination.
            window.KeyPressQwerty(PhysicalKey.ControlLeft, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.ShiftLeft, RawInputModifiers.Control);
            Dispatcher.UIThread.RunJobs();
            Assert.True(viewModel.IsCapturingKey, "a modifier on its own finished the capture");

            window.KeyPressQwerty(PhysicalKey.S, RawInputModifiers.Control | RawInputModifiers.Shift);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Ctrl+Shift+S", viewModel.BindKey);
            Assert.Equal("Ctrl+Shift+S", viewModel.BindKeyDisplay);
            Assert.False(viewModel.IsCapturingKey);

            // Letting the keys go afterwards binds nothing again: the capture was already over.
            window.KeyReleaseQwerty(PhysicalKey.S, RawInputModifiers.Control | RawInputModifiers.Shift);
            window.KeyReleaseQwerty(PhysicalKey.ShiftLeft, RawInputModifiers.Control);
            window.KeyReleaseQwerty(PhysicalKey.ControlLeft, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Ctrl+Shift+S", viewModel.BindKey);
        });
    }

    [Fact]
    public void A_modifier_held_before_the_capture_started_still_counts()
    {
        Ui.Run(() =>
        {
            var (window, viewModel) = Open();
            viewModel.SetKeyCommand.Execute(null);

            // Ctrl went down before the button was pressed, so this window never saw it go down;
            // only the event itself can say that it is held.
            window.KeyPressQwerty(PhysicalKey.S, RawInputModifiers.Control);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Ctrl+S", viewModel.BindKey);
            Assert.False(viewModel.IsCapturingKey);
        });
    }

    [Fact]
    public void A_modifier_that_nothing_follows_is_the_binding_on_its_own()
    {
        Ui.Run(() =>
        {
            var (window, viewModel) = Open();
            viewModel.SetKeyCommand.Execute(null);

            window.KeyPressQwerty(PhysicalKey.ControlLeft, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.True(viewModel.IsCapturingKey);

            // Let go of with nothing pressed in between: that is the binding, kept with the side it
            // was pressed on, the way a trigger bound to Right Shift reads.
            window.KeyReleaseQwerty(PhysicalKey.ControlLeft, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("左Ctrl", viewModel.BindKey);
            Assert.False(viewModel.IsCapturingKey);
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

            var rows = window.FindControl<ListBox>("StepList")!.GetVisualDescendants().OfType<ListBoxItem>().ToList();
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
            var tip = Assert.IsType<Border>(mark.Parent);
            Assert.Equal(28, tip.Width);
            Assert.Equal(28, tip.Height);
            Assert.NotNull(tip.Background);
            Assert.Contains("break", ToolTip.GetTip(tip) as string ?? string.Empty);

            // The line above the list says there is something to look at.
            var summary = Assert.Single(window.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Classes.Contains("ProblemSummary") && text.IsVisible);
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

            var tip = Assert.IsType<Border>(Assert.Single(Marks(window)).Parent);
            Assert.False(tip.IsVisible);
        });
    }

    /// <summary>The warning mark every row carries, whether or not it is showing.</summary>
    private static List<Control> Marks(Window window)
        => [.. window.GetVisualDescendants().OfType<Control>()
            .Where(control => control.Classes.Contains("ProblemMark"))];

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

    [Fact]
    public void A_macro_whose_declarations_clash_says_so_above_the_steps()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "甲" };
            macro.Parameters.Add(new MacroParameter { Name = "同名" });
            macro.Parameters.Add(new MacroParameter { Name = "同名" });
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (MacroEditorViewModel)window.DataContext!;
            Assert.True(viewModel.HasDeclarationProblems);
            Assert.Contains("同名", viewModel.DeclarationProblemSummary, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_declaration_warning_does_not_block_saving_unfinished_work()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "还没写完" };
            macro.Results.Add(new MacroResult { Name = "答案" });
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (MacroEditorViewModel)window.DataContext!;
            Assert.True(viewModel.HasDeclarationProblems);
            Assert.False(viewModel.HasBlockingDeclarationProblems);
            Assert.True(viewModel.CanSave);

            MacroItem? saved = null;
            viewModel.CloseRequested += result => saved = result;
            viewModel.SaveCommand.Execute(null);

            Assert.NotNull(saved);
            Assert.Equal("答案", Assert.Single(saved!.Results).Name);
        });
    }

    [Fact]
    public void A_macro_name_has_to_be_there_and_belong_to_one_macro()
    {
        Ui.Run(() =>
        {
            var first = new MacroItem { Name = "甲" };
            var second = new MacroItem { Name = "乙" };
            var window = new MacroEditorWindow(first, [first, second]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (MacroEditorViewModel)window.DataContext!;
            Assert.False(viewModel.HasNameProblem);

            viewModel.Name = "乙";
            Assert.True(viewModel.HasNameProblem);
            Assert.Equal(Strings.Format("Editor.Problem.DuplicateMacroName", "乙"),
                viewModel.NameProblem);

            viewModel.Name = " ";
            Assert.True(viewModel.HasNameProblem);
            Assert.Equal(Strings.Get("Editor.Problem.EmptyMacroName"), viewModel.NameProblem);

            var closed = false;
            viewModel.CloseRequested += _ => closed = true;
            viewModel.SaveCommand.Execute(null);
            Assert.False(closed);
        });
    }

    [Fact]
    public void The_action_editor_is_a_window_and_not_an_embedded_inspector()
    {
        Ui.Run(() =>
        {
            var (window, viewModel) = Open();
            Assert.Null(window.FindControl<Border>("InspectorHost"));
            Assert.Null(window.FindControl<ContentControl>("InspectorContent"));
            Assert.NotNull(window.FindControl<Border>("MacroSettingsDrawer"));

            var action = new ActionEditorWindow(null, ActionCatalog.Definitions, [], []);
            Assert.IsType<ActionEditorView>(action.Content);
            Assert.False(action.StaysOpen);
            action.Close();
        });
    }

    [Fact]
    public void The_inspector_can_edit_the_macro_interface()
    {
        Ui.Run(() =>
        {
            var (window, viewModel) = Open();

            var host = window.FindControl<ContentControl>("MacroInterfaceHost");
            Assert.NotNull(host);
            Assert.True(host!.IsVisible);

            var view = Assert.IsType<MacroInterfaceView>(host.Content);
            var model = (MacroInterfaceViewModel)view.DataContext!;
            model.AddParameterCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            var row = Assert.Single(model.Parameters);
            row.Name = "数量";
            row.SelectedType = model.Types.First(type => type.Value == ActionReturnType.Number);
            Dispatcher.UIThread.RunJobs();

            var declared = Assert.Single(viewModel.DeclaredParameters);
            Assert.Equal("数量", declared.Name);
            Assert.Equal(ActionReturnType.Number, declared.Type);
            Assert.True(viewModel.IsDirty);
        });
    }

    [Fact]
    public void The_inspector_can_say_what_a_record_holds()
    {
        Ui.Run(() =>
        {
            var (window, viewModel) = Open();
            var host = window.FindControl<ContentControl>("MacroInterfaceHost");
            Assert.NotNull(host);
            var view = Assert.IsType<MacroInterfaceView>(host!.Content);
            var model = (MacroInterfaceViewModel)view.DataContext!;

            model.AddParameterCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            var row = Assert.Single(model.Parameters);
            row.Name = "点";

            // Fields only mean something for a record, so the rows wait for the kind to say so.
            Assert.False(row.IsRecord);
            row.SelectedType = model.Types.First(type => type.Value == ActionReturnType.Record);
            Assert.True(row.IsRecord);

            row.AddFieldCommand.Execute(null);
            var field = Assert.Single(row.Fields);
            field.Name = "x";
            field.SelectedType = model.Types.First(type => type.Value == ActionReturnType.Number);
            Dispatcher.UIThread.RunJobs();

            var declared = Assert.Single(viewModel.DeclaredParameters);
            var written = Assert.Single(declared.Fields);
            Assert.Equal("x", written.Name);
            Assert.Equal(ActionReturnType.Number, written.Type);

            // A field row is a declaration edit like any other: the variable wall and the check that
            // reads field(...) both answer differently once it is written down.
            Assert.True(viewModel.IsDirty);

            row.RemoveFieldCommand.Execute(field);
            Assert.Empty(declared.Fields);
        });
    }

    [Fact]
    public void The_macro_interface_offers_expression_and_variable_tools_for_a_default()
    {
        Ui.Run(() =>
        {
            var (window, _) = Open();
            var host = window.FindControl<ContentControl>("MacroInterfaceHost");
            Assert.NotNull(host);
            var view = Assert.IsType<MacroInterfaceView>(host!.Content);
            var model = (MacroInterfaceViewModel)view.DataContext!;

            model.AddParameterCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            var buttons = view.GetVisualDescendants().OfType<Button>()
                .Select(button => button.Content?.ToString())
                .ToList();
            Assert.Contains(Strings.Get("Add.ExpressionBuilder"), buttons);
            Assert.Contains(Strings.Get("Add.VariablePicker"), buttons);
            Assert.Contains(model.ValueVariables, choice => choice.Name == "sys.date");
        });
    }

    [Fact]
    public void The_inspector_can_change_the_order_of_the_declarations()
    {
        Ui.Run(() =>
        {
            var (window, viewModel) = Open();
            var host = window.FindControl<ContentControl>("MacroInterfaceHost");
            Assert.NotNull(host);
            var view = Assert.IsType<MacroInterfaceView>(host!.Content);
            var model = (MacroInterfaceViewModel)view.DataContext!;

            model.AddParameterCommand.Execute(null);
            model.AddParameterCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            var first = model.Parameters[0];
            var second = model.Parameters[1];
            first.Name = "甲";
            second.Name = "乙";

            // The order is the order a call form asks for the values in, so moving one has to change
            // what is saved rather than only what is drawn.
            Assert.False(first.CanMoveUp);
            Assert.True(first.CanMoveDown);
            Assert.True(second.CanMoveUp);
            Assert.False(second.CanMoveDown);

            model.MoveParameterUpCommand.Execute(second);
            Assert.Equal(["乙", "甲"], model.Parameters.Select(row => row.Name));
            Assert.Equal(["乙", "甲"], viewModel.DeclaredParameters.Select(parameter => parameter.Name));

            // At the top there is nothing to swap with, so the button is off and the command is a
            // no-op rather than an error.
            model.MoveParameterUpCommand.Execute(model.Parameters[0]);
            Assert.Equal(["乙", "甲"], viewModel.DeclaredParameters.Select(parameter => parameter.Name));

            model.AddResultCommand.Execute(null);
            model.AddResultCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            model.Results[0].Name = "一";
            model.Results[1].Name = "二";

            model.MoveResultDownCommand.Execute(model.Results[0]);
            Assert.Equal(["二", "一"], viewModel.DeclaredResults.Select(result => result.Name));
        });
    }

    [Fact]
    public void An_invalid_declaration_keeps_the_editor_open()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "坏声明" };
            macro.Parameters.Add(new MacroParameter { Name = "同名" });
            macro.Parameters.Add(new MacroParameter { Name = "同名" });
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (MacroEditorViewModel)window.DataContext!;
            Assert.False(viewModel.CanSave);
            Assert.True(viewModel.HasBlockingDeclarationProblems);

            var closed = false;
            viewModel.CloseRequested += _ => closed = true;
            viewModel.SaveCommand.Execute(null);

            Assert.False(closed);
        });
    }

    [Fact]
    public void A_second_declaration_edit_still_reaches_the_recovery_writer()
    {
        Ui.Run(() =>
        {
            var (window, viewModel) = Open();
            var host = window.FindControl<ContentControl>("MacroInterfaceHost");
            Assert.NotNull(host);
            var model = (MacroInterfaceViewModel)Assert.IsType<MacroInterfaceView>(host!.Content).DataContext!;

            var changes = 0;
            viewModel.Changed += (_, _) => changes++;

            model.AddParameterCommand.Execute(null);
            var afterAdd = changes;
            var row = Assert.Single(model.Parameters);
            row.Name = "第一";
            row.Name = "第二";

            // MarkDirty raises Changed even while the macro is already unsaved; without that the
            // second edit would never be written to the recovery snapshot.
            Assert.True(changes >= afterAdd + 2,
                $"only {changes - afterAdd} change(s) followed the add");
        });
    }

    [Fact]
    public void Renaming_a_parameter_names_the_macro_that_would_break()
    {
        Ui.Run(() =>
        {
            var caller = new MacroItem { Name = "调用者" };
            caller.Steps.Add(new MacroStep
            {
                Type = "control.runMacro",
                Meta = StepMeta.New,
                Parameters =
                [
                    new StepParameter
                    {
                        Name = "macro",
                        Kind = ActionParameterKind.Macro,
                        Value = "被调用",
                    },
                    new StepParameter
                    {
                        Name = "arg.旧",
                        Kind = ActionParameterKind.Expression,
                        Value = "1",
                    },
                ],
            });

            var edited = new MacroItem { Name = "被调用" };
            edited.Parameters.Add(new MacroParameter { Name = "新", Required = true });
            var window = new MacroEditorWindow(edited, [edited, caller]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (MacroEditorViewModel)window.DataContext!;
            Assert.True(viewModel.HasCallImpact);
            Assert.Contains("调用者", viewModel.CallImpactSummary, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_self_call_is_checked_against_the_macros_own_steps()
    {
        Ui.Run(() =>
        {
            var start = new MacroStep
            {
                Type = "control.startMacro",
                Meta = StepMeta.New,
                Parameters =
                [
                    new StepParameter
                    {
                        Name = "macro",
                        Kind = ActionParameterKind.Macro,
                        Value = "自己",
                    },
                    new StepParameter
                    {
                        Name = "runHandleName",
                        Kind = ActionParameterKind.Variable,
                        Value = "job",
                    },
                ],
            };
            var hold = new MacroStep
            {
                Type = "control.holdBrowser",
                Meta = StepMeta.New,
                Parameters =
                [
                    new StepParameter
                    {
                        Name = "steps",
                        Kind = ActionParameterKind.Steps,
                        Steps = [start],
                    },
                ],
            };
            var browse = new MacroStep
            {
                Type = "browser.goTo",
                Meta = StepMeta.New,
                Parameters =
                [
                    new StepParameter
                    {
                        Name = "url",
                        Kind = ActionParameterKind.Text,
                        Value = "https://example.com",
                    },
                ],
            };
            var macro = new MacroItem { Name = "自己" };
            macro.Steps.Add(hold);
            macro.Steps.Add(browse);

            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (MacroEditorViewModel)window.DataContext!;
            Assert.Contains(viewModel.Problems, problem =>
                problem.Message == Strings.Get("Editor.Problem.BrowserRunWhileHeld"));
        });
    }

    [Fact]
    public void The_left_palette_has_four_sources()
    {
        Ui.Run(() =>
        {
            var (window, _) = Open();
            var palette = Assert.IsType<ActionPaletteView>(
                window.FindControl<ActionPaletteView>("ActionPalette"));
            var model = Assert.IsType<ActionPaletteViewModel>(palette.DataContext);

            Assert.Equal(4, model.Tabs.Count);
            model.SelectDock(ActionPaletteDock.Basic);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(model.Cards, card => card.Key == "input.keyPress");
            Assert.DoesNotContain(model.Cards, card => card.Key.StartsWith("control."));

            model.SelectDock(ActionPaletteDock.Flow);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(model.Cards, card => card.Key == "control.repeat");
            Assert.All(model.Cards, card => Assert.StartsWith("control.", card.Key));
        });
    }

    [Fact]
    public void Both_seams_of_the_editor_columns_have_a_splitter()
    {
        Ui.Run(() =>
        {
            var (window, _) = Open();
            var grid = Assert.IsType<Grid>(window.FindControl<Grid>("EditorColumns"));

            Assert.Equal(5, grid.ColumnDefinitions.Count);
            Assert.Equal(280d, grid.ColumnDefinitions[0].MinWidth);
            Assert.Equal(280d, grid.ColumnDefinitions[0].Width.Value);
            Assert.Equal(480d, grid.ColumnDefinitions[2].MinWidth);
            Assert.Equal(360d, grid.ColumnDefinitions[4].MinWidth);
            Assert.Equal(360d, grid.ColumnDefinitions[4].Width.Value);

            var splitters = grid.Children.OfType<GridSplitter>().ToList();
            Assert.Equal(2, splitters.Count);
            Assert.Contains(splitters, splitter => Grid.GetColumn(splitter) == 1);
            Assert.Contains(splitters, splitter => Grid.GetColumn(splitter) == 3);
        });
    }
}
