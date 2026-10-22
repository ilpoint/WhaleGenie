using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The breakpoint's menu, driven the way a user drives it: the step's row is right-clicked — the dot
/// itself carries no menu, so right-clicking that lands on the row's — and what the menu offers is
/// really opened, filled in and taken up. A menu item pointing at the wrong handler looks right in
/// the markup and does nothing when it is clicked, so the wiring is checked here rather than read.
/// </summary>
public class RunBreakpointTests
{
    [Fact]
    public void The_condition_written_here_arms_the_breakpoint_with_it()
    {
        Ui.Run(() =>
        {
            var (window, viewModel, row) = Open(Set("n", "1"));

            Click(window, row, "condition");

            // The form is the editor's ordinary if form: its condition field is the one expression
            // a breakpoint asks before the step it belongs to runs.
            var dialog = window.OwnedWindows.OfType<ActionEditorWindow>().Single();
            var form = (AddActionViewModel)dialog.DataContext!;
            Assert.All(form.AvailableActions,
                action => Assert.Equal("control.if", action.Key));

            form.SelectAction("control.if");
            Dispatcher.UIThread.RunJobs();
            form.Parameters.First(parameter => parameter.Definition.Name == "condition").Text = "$n > 3";
            Assert.True(form.CanSave, form.ValidationMessage);
            form.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();

            // The dialog is gone and the step's dot is on: setting a condition on a step that had
            // no breakpoint means "stop here when this holds".
            Assert.Empty(window.OwnedWindows.OfType<ActionEditorWindow>());
            Assert.True(row.HasBreakpoint);
            Assert.Equal("control.if", viewModel.BreakpointCondition(row.Id)!.Type);

            // And the row says what it waits for, so a run that goes past it is not a mystery.
            Assert.Contains("$n > 3", row.ConditionNote, System.StringComparison.Ordinal);
            Assert.True(row.HasCondition);

            // Opening it again shows what was written rather than a blank form.
            Click(window, row, "condition");
            var again = (AddActionViewModel)window.OwnedWindows.OfType<ActionEditorWindow>().Single().DataContext!;
            Assert.Equal("control.if", again.SelectedDefinition!.Key);
            Assert.Equal("$n > 3", again.Parameters
                .First(parameter => parameter.Definition.Name == "condition").Text);
            again.CancelCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            // Clearing the condition leaves the breakpoint standing: it is back to stopping every
            // time it is reached.
            Click(window, row, "clear");
            Assert.Null(viewModel.BreakpointCondition(row.Id));
            Assert.True(row.HasBreakpoint);
            Assert.False(row.HasCondition);

            window.Close();
        });
    }

    /// <summary>A condition expression belongs to its block and does not become a run step.</summary>
    [Fact]
    public void A_condition_expression_does_not_add_a_row()
    {
        Ui.Run(() =>
        {
            var (window, viewModel, step) = Open(new MacroStep
            {
                Type = "control.if",
                Parameters =
                [
                    new StepParameter
                    {
                        Name = "condition",
                        Kind = ActionParameterKind.Expression,
                        Value = "$n == 1",
                    },
                    new StepParameter
                    {
                        Name = "then",
                        Kind = ActionParameterKind.Steps,
                        Steps = [Set("m", "1")],
                    },
                ],
            });

            Assert.Equal(2, viewModel.Steps.Count);
            Assert.True(step.CanBreak);
            Assert.True(viewModel.Steps[1].CanBreak);
            var dots = window.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Classes.Contains("Breakpoint") && button.IsEffectivelyVisible)
                .ToList();

            Assert.Equal(2, dots.Count);
            Assert.Contains(dots, dot => ReferenceEquals(dot.DataContext, step));

            window.Close();
        });
    }

    /// <summary>
    /// 断点的三件事都在步骤行的右键菜单里：下断点、设置条件、清除条件。圆点自己不再挂菜单 —— 它只有
    /// 16 像素宽，挂在它上面的菜单大多数人找不到，"设置断点条件"更是难按出来；右键圆点沿控件树落到这
    /// 一行上，弹出来的还是这一份。
    /// </summary>
    [Fact]
    public void The_row_menu_carries_the_breakpoint_and_the_dot_falls_through_to_it()
    {
        Ui.Run(() =>
        {
            var (window, viewModel, row) = Open(Set("n", "1"));

            var line = Row(window, row);
            var dot = Dot(window, row);

            // 圆点自己没有菜单，右键它落到行的那一份上。
            Assert.Null(dot.ContextMenu);
            Assert.False(line.ContextMenu!.IsOpen);
            dot.RaiseEvent(new ContextRequestedEventArgs());
            Dispatcher.UIThread.RunJobs();
            Assert.True(line.ContextMenu.IsOpen, "右键圆点该弹出那一步的行菜单");

            var items = line.ContextMenu.Items.OfType<MenuItem>().ToList();
            Assert.Contains(items, item => Equals(item.Tag, "toggle"));
            Assert.Contains(items, item => Equals(item.Tag, "condition"));
            Assert.Contains(items, item => Equals(item.Tag, "clear"));

            // 没有断点时给的是「下断点」，也没有条件可清；「跳过这一步」不在这一份菜单里 —— 那是工具
            // 栏上那枚按钮的位置。
            Assert.Equal(Strings.Get("Run.BreakpointSet"), On(items, "toggle", "Run.BreakpointSet").Header);
            Assert.False(Shown(items, "toggle", "Run.BreakpointOff"));
            Assert.True(Shown(items, "condition", "Run.BreakpointCondition"));
            Assert.False(Shown(items, "clear", "Run.BreakpointClear"));
            Assert.DoesNotContain(items, item => Equals(item.Tag, "skip"));

            // 点一下「下断点」：断点真的下上了，标题换成「取消断点」。
            Click(window, row, "toggle");
            Assert.True(row.HasBreakpoint);

            var after = line.ContextMenu.Items.OfType<MenuItem>().ToList();
            Assert.True(Shown(after, "toggle", "Run.BreakpointOff"));
            Assert.False(Shown(after, "toggle", "Run.BreakpointSet"));
            Assert.False(Shown(after, "clear", "Run.BreakpointClear"));

            // 再点一下：断点收回去。
            Click(window, row, "toggle");
            Assert.False(row.HasBreakpoint);
            Assert.Equal(Strings.Get("Run.BreakpointSet"),
                On(line.ContextMenu.Items.OfType<MenuItem>(), "toggle", "Run.BreakpointSet").Header);

            window.Close();
        });
    }

    /// <summary>
    /// 点一下圆点就该立刻变红：先前红色被主题画在悬停与按下态上，指针还停在圆点上的时候（也就是刚点完
    /// 那一刻）看不出任何变化，要等鼠标移开才亮。
    /// </summary>
    [Fact]
    public void The_dot_turns_on_the_moment_it_is_clicked()
    {
        Ui.Run(() =>
        {
            var (window, _, row) = Open(Set("n", "1"));
            var dot = Dot(window, row);

            Assert.DoesNotContain("on", dot.Classes);

            dot.Command!.Execute(dot.CommandParameter);
            Dispatcher.UIThread.RunJobs();

            Assert.True(row.HasBreakpoint);
            Assert.Contains("on", dot.Classes);

            window.Close();
        });
    }

    /// <summary>
    /// 红色得压过悬停与按下的颜色：主题把这两种态画在 ContentPresenter 上，只给 Button 自己上色的话
    /// 点完还是看不出来。这条按写下来的样式读，因为悬停态在无头会话里没有指针可动。
    /// </summary>
    [Fact]
    public void The_red_of_a_breakpoint_beats_the_hover_colour()
    {
        var markup = File.ReadAllText(
            Path.Combine(Repository(), "WhaleGenie", "Views", "RunWindow.axaml"));

        foreach (var state in new[] { "", ":pointerover", ":pressed" })
        {
            var selector = $"Button.Breakpoint.on{state} /template/ ContentPresenter";
            var at = markup.IndexOf(selector, StringComparison.Ordinal);
            Assert.True(at >= 0, $"少了这样一条样式：{selector}");
            Assert.Contains("#E06C75", markup[at..(at + 240)], StringComparison.Ordinal);
        }
    }

    /// <summary>Whether a menu item is drawn, read off its own visibility binding.</summary>
    private static bool Shown(IEnumerable<MenuItem> items, string tag, string key)
        => On(items, tag, key).IsVisible;

    /// <summary>The one item of a kind whose header is the word of that key.</summary>
    private static MenuItem On(IEnumerable<MenuItem> items, string tag, string key)
        => items.Single(item => Equals(item.Tag, tag) && Equals(item.Header, Strings.Get(key)));

    /// <summary>The breakpoint dot on a step's row.</summary>
    private static Button Dot(RunWindow window, RunStepViewModel row)
        => window.GetVisualDescendants().OfType<Button>()
            .Single(button => button.Classes.Contains("Breakpoint")
                && ReferenceEquals(button.DataContext, row));

    /// <summary>
    /// Skipping belongs to a run that is standing in front of a step, and the command is the toolbar
    /// button's — the row menu no longer offers it. Asked at any other moment it must do nothing:
    /// leaving the flag set would leave out whichever step ran next.
    /// </summary>
    [Fact]
    public void Skipping_while_no_run_is_paused_leaves_the_next_step_alone()
    {
        var answer = Ui.RunAsync(async () =>
        {
            var (window, viewModel, row) = Open(Set("n", "1"), Set("m", "1"));

            Assert.False(viewModel.SkipStepCommand.CanExecute(null));
            viewModel.SkipStepCommand.Execute(null);

            viewModel.RunCommand.Execute(null);
            while (viewModel.IsBusy)
            {
                await Task.Delay(10);
            }

            // A run says it has finished as soon as the last step is done, and what that step came
            // to reaches the rows a moment later, on this same thread. Reading the rows without
            // letting that arrive first is what made this check pass or fail on its own timing.
            Dispatcher.UIThread.RunJobs();

            return viewModel.Steps.Select(step => step.HasRun).ToList();
        });

        Assert.Equal([true, true], answer);
    }

    /// <summary>A run window on steps that do nothing but set a variable, with no devices behind it.</summary>
    private static (RunWindow Window, RunViewModel ViewModel, RunStepViewModel Row) Open(
        params MacroStep[] steps)
    {
        StepIds.Settle(steps);

        var window = new RunWindow(steps, NullDeviceLayer.Instance);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var viewModel = (RunViewModel)window.DataContext!;
        return (window, viewModel, viewModel.Steps[0]);
    }

    /// <summary>
    /// Right-clicks a step's row and picks one of the things the breakpoint menu offers. The row is
    /// what carries the menu: the dot on it is 16 pixels wide, and a menu only that small target
    /// opens is a menu most people never find.
    /// </summary>
    private static void Click(RunWindow window, RunStepViewModel row, string tag)
    {
        var line = Row(window, row);

        line.ContextMenu!.Open(line);
        Dispatcher.UIThread.RunJobs();

        // 同一件事的两个标题（下断点 / 取消断点）各占一项，画出来的那一项才是能点的那一项。
        var item = line.ContextMenu.Items.OfType<MenuItem>()
            .Single(entry => Equals(entry.Tag, tag) && entry.IsVisible);

        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The row a step is written on, which is where the breakpoint menu hangs.</summary>
    private static Border Row(RunWindow window, RunStepViewModel row)
        => window.GetVisualDescendants().OfType<Border>()
            .First(border => border.Classes.Contains("RunRow")
                && ReferenceEquals(border.DataContext, row));

    /// <summary>A step that sets a variable, so a run has something to do that cannot fail.</summary>
    private static MacroStep Set(string name, string value) => new()
    {
        Type = "control.setVariable",
        Parameters =
        [
            new StepParameter { Name = "name", Kind = ActionParameterKind.Text, Value = name },
            new StepParameter { Name = "scope", Kind = ActionParameterKind.Text, Value = "local" },
            new StepParameter { Name = "value", Kind = ActionParameterKind.Text, Value = value },
        ],
    };

    /// <summary>
    /// The repository root, found by walking up from the test binaries until the solution file is
    /// there, so the markup a check reads is the markup that was built.
    /// </summary>
    private static string Repository()
    {
        for (var at = new DirectoryInfo(AppContext.BaseDirectory); at is not null; at = at.Parent)
        {
            if (File.Exists(Path.Combine(at.FullName, "WhaleGenie.slnx")))
            {
                return at.FullName;
            }
        }

        throw new InvalidOperationException("WhaleGenie.slnx was not found above the test binaries.");
    }
}
