using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// Dropping an action: into an empty body, onto a block row, and the band that says where.
public partial class MacroEditorWorkspaceTests
{
    /// <summary>
    /// Dropping into an empty body has to land inside it: the band says "inside this block", the
    /// form opens, and saving puts the action in the body rather than in front of the loop.
    /// </summary>
    [Fact]
    public void Dropping_on_an_empty_body_puts_the_action_inside_it()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var editor = (MacroEditorViewModel)window.DataContext!;
            var loop = Step("control.repeat",
                new StepParameter { Name = "times", Kind = ActionParameterKind.Number, Written = Formula.Number(2) },
                new StepParameter { Name = "body", Kind = ActionParameterKind.Steps });
            editor.AddStep(loop);
            Dispatcher.UIThread.RunJobs();

            var list = window.FindControl<TreeView>("StepList")!;
            var block = Header(list, row => row.IsStep && row.Step.Type == "control.repeat");
            var middle = block.TranslatePoint(
                new Point(60, block.Bounds.Height * 0.75), list)!.Value;
            var transfer = new ActionDragPayload(
                ActionPaletteCardKind.Action, "control.delay").Transfer();

            DragAt(list, DragDrop.DropEvent, transfer, middle);
            Dispatcher.UIThread.RunJobs();

            var action = Assert.Single(window.OwnedWindows.OfType<ActionEditorWindow>());
            var form = (AddActionViewModel)action.DataContext!;
            form.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            var saved = Assert.Single(editor.Steps);
            var body = saved.StepLists.Single().Steps;
            Assert.Single(body);
            Assert.Equal("control.delay", body[0].Type);

            Ui.CloseAndAnswer(window);
        });
    }

    [Fact]
    public void An_empty_block_body_is_a_single_drop_target()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var editor = (MacroEditorViewModel)window.DataContext!;
            editor.AddStep(Step("control.repeat",
                new StepParameter { Name = "times", Kind = ActionParameterKind.Number, Written = Formula.Number(2) },
                new StepParameter { Name = "body", Kind = ActionParameterKind.Steps }));
            Dispatcher.UIThread.RunJobs();

            var list = window.FindControl<TreeView>("StepList")!;
            var marker = window.FindControl<Border>("DropMarker")!;
            var hint = window.FindControl<TextBlock>("DropHintText")!;
            var head = Header(list, row => row.IsBlock);
            var top = head.TranslatePoint(new Point(0, head.Bounds.Height / 2), list)!.Value.Y;
            var bottom = head.TranslatePoint(new Point(0, head.Bounds.Height), list)!.Value.Y;
            var transfer = new ActionDragPayload(
                ActionPaletteCardKind.Action, "control.log").Transfer();

            // Every two pixels of the block row's lower half, so a gap cannot hide a second answer.
            var seen = new List<(string? Hint, double? Marker)>();
            for (var y = top; y < bottom; y += 2)
            {
                DragAt(list, DragDrop.DragOverEvent, transfer, new Point(60, y));
                seen.Add((hint.Text, Canvas.GetTop(marker)));
            }

            Assert.True(seen.Count > 4);
            Assert.All(seen, entry => Assert.Equal(Strings.Get("Editor.DropIntoEmptyList"), entry.Hint));
            Assert.Single(seen.Select(entry => entry.Marker).Distinct());

            Ui.CloseAndAnswer(window);
        });
    }

    /// <summary>
    /// Once the body holds a step, the heights of the loop row mean different things again — before
    /// the block, or inside it at the end — and the band says which.
    /// </summary>
    [Fact]
    public void A_block_row_puts_the_step_at_the_end_of_its_body()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var editor = (MacroEditorViewModel)window.DataContext!;
            editor.AddStep(Step("control.repeat",
                new StepParameter { Name = "times", Kind = ActionParameterKind.Number, Written = Formula.Number(2) },
                new StepParameter
                {
                    Name = "body",
                    Kind = ActionParameterKind.Steps,
                    Steps = [Step("control.delay",
                        new StepParameter { Name = "ms", Kind = ActionParameterKind.Number, Written = Formula.Number(1) })],
                }));
            Dispatcher.UIThread.RunJobs();

            var list = window.FindControl<TreeView>("StepList")!;
            var hint = window.FindControl<TextBlock>("DropHintText")!;
            var block = Header(list, row => row.IsStep && row.IsBlock);
            var transfer = new ActionDragPayload(
                ActionPaletteCardKind.Action, "control.log").Transfer();

            // The whole of a block's row means the same thing: put it in this block, at the end.
            // That is what a body row is for, and it does not depend on which half was aimed at.
            foreach (var fraction in new[] { 0.2, 0.5, 0.9 })
            {
                DragAt(list, DragDrop.DragOverEvent, transfer,
                    block.TranslatePoint(new Point(60, block.Bounds.Height * fraction), list)!.Value);
                Assert.Equal(Strings.Get("Editor.DropAtBlockEnd"), hint.Text);
            }

            Ui.CloseAndAnswer(window);
        });
    }

    [Fact]
    public void The_drop_band_says_where_the_action_will_land()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var list = window.FindControl<TreeView>("StepList")!;
            var marker = window.FindControl<Border>("DropMarker")!;
            var hint = window.FindControl<TextBlock>("DropHintText")!;
            var transfer = new ActionDragPayload(
                ActionPaletteCardKind.Action, "control.log").Transfer();

            DragAt(list, DragDrop.DragOverEvent, transfer, new Point(40, 40));
            Assert.True(marker.IsVisible);
            Assert.True(marker.Height >= 64);
            Assert.Equal(Strings.Get("Editor.DropIntoEmptyList"), hint.Text);

            DragAt(list, DragDrop.DragLeaveEvent, transfer, new Point(40, 40));
            Assert.False(marker.IsVisible);

            var loop = Step("control.repeat",
                new StepParameter
                {
                    Name = "times",
                    Kind = ActionParameterKind.Number,
                    Written = Formula.Number(2),
                },
                new StepParameter
                {
                    Name = "body",
                    Kind = ActionParameterKind.Steps,
                    Steps = [Step("control.log")],
                });
            var model = (MacroEditorViewModel)window.DataContext!;
            model.AddStep(loop);
            Dispatcher.UIThread.RunJobs();

            var block = Header(list, row => row.IsStep && row.IsBlock);
            var center = block.TranslatePoint(
                new Point(block.Bounds.Width / 2, block.Bounds.Height / 2), list);
            Assert.NotNull(center);
            DragAt(list, DragDrop.DragOverEvent, transfer, center!.Value);

            Assert.True(marker.IsVisible);
            Assert.Equal(Strings.Get("Editor.DropAtBlockEnd"), hint.Text);
            Assert.True(Canvas.GetLeft(marker) > 0);

            Ui.CloseAndAnswer(window);
        });
    }

    /// <summary>
    /// 在外层循环的循环体里新建一个计数循环：表单里默认的计数器名字换成外层没在用的那个，保存下来
    /// 就是它。表单问的是编辑器此刻的落点，与步骤真正落进去的那一层是同一个答案。
    /// </summary>
    [Fact]
    public void A_counting_loop_dropped_inside_another_takes_a_counter_of_its_own()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var editor = (MacroEditorViewModel)window.DataContext!;
            var outer = Step("control.for",
                new StepParameter
                {
                    Name = "name",
                    Kind = ActionParameterKind.Text,
                    Written = Formula.Name("i"),
                },
                new StepParameter
                {
                    Name = "body",
                    Kind = ActionParameterKind.Steps,
                    Steps = [Step("control.log")],
                });
            editor.AddStep(outer);
            Dispatcher.UIThread.RunJobs();

            var list = window.FindControl<TreeView>("StepList")!;
            var body = Header(list, row => row.IsSection);
            var middle = body.TranslatePoint(
                new Point(body.Bounds.Width / 2, body.Bounds.Height / 2), list)!.Value;
            var transfer = new ActionDragPayload(
                ActionPaletteCardKind.Action, "control.for").Transfer();

            DragAt(list, DragDrop.DropEvent, transfer, middle);
            Dispatcher.UIThread.RunJobs();

            var action = Assert.Single(window.OwnedWindows.OfType<ActionEditorWindow>());
            var form = (AddActionViewModel)action.DataContext!;
            var counter = form.Parameters.First(parameter => parameter.Definition.Name == "name");
            Assert.Equal("i2", counter.Text);

            form.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();

            var inner = outer.StepLists.Single().Steps[^1];
            Assert.Equal("control.for", inner.Type);
            Assert.Equal("i2", inner.Text("name"));

            // 借了外层的名字才会被标出来；按这个名字写的不会。
            Assert.DoesNotContain(editor.Problems, problem =>
                problem.Step == inner && problem.Code == MacroProblemCode.LoopVariableShadowed);

            Ui.CloseAndAnswer(window);
        });
    }
}
