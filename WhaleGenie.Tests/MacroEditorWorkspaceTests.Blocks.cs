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
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// An empty block body as a drop target, at every pixel and after a step.
public partial class MacroEditorWorkspaceTests
{
    /// <summary>
    /// A loop whose body is still empty is one place, not four: its own row, the title of the body
    /// and the line that closes it all put the action in the same slot, so the band does not jump
    /// between four heights while the pointer moves down an empty body.
    /// </summary>
    /// <summary>
    /// Every pixel of an empty block answers the same way, walked one pixel at a time: the gaps
    /// between its rows belong to the row above, not to nothing, so the band does not flicker as
    /// the pointer goes down.
    /// </summary>
    [Fact]
    public void An_empty_block_body_answers_the_same_way_at_every_pixel()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var editor = (MacroEditorViewModel)window.DataContext!;
            editor.AddStep(Step("control.repeat",
                new StepParameter { Name = "times", Kind = ActionParameterKind.Number, Value = "2" },
                new StepParameter { Name = "body", Kind = ActionParameterKind.Steps }));
            Dispatcher.UIThread.RunJobs();

            var list = window.FindControl<TreeView>("StepList")!;
            var hint = window.FindControl<TextBlock>("DropHintText")!;
            var marker = window.FindControl<Border>("DropMarker")!;
            var head = Header(list, row => row.IsBlock);
            // The lower half of the block's own row is its empty body: the upper half is still
            // "before the block", and there is no closing line to walk down to any more.
            var top = head.TranslatePoint(new Point(0, head.Bounds.Height / 2), list)!.Value.Y;
            var bottom = head.TranslatePoint(new Point(0, head.Bounds.Height), list)!.Value.Y;
            var transfer = new ActionDragPayload(
                ActionPaletteCardKind.Action, "control.log").Transfer();

            var inside = new List<(string? Hint, double? Marker)>();
            for (var y = top; y < bottom; y += 1)
            {
                DragAt(list, DragDrop.DragOverEvent, transfer, new Point(60, y));
                inside.Add((hint.Text, Canvas.GetTop(marker)));
            }

            Assert.True(inside.Count > 20, $"only {inside.Count} pixels sampled");
            Assert.All(inside, entry => Assert.Equal(Strings.Get("Editor.DropIntoEmptyList"), entry.Hint));
            Assert.Single(inside.Select(entry => entry.Marker).Distinct());

            Ui.CloseAndAnswer(window);
        });
    }

    /// <summary>
    /// The same walk with the loop sitting after another step, which is how one is usually added:
    /// nothing between the step above and the closing line of the loop may answer "before the
    /// block".
    /// </summary>
    [Fact]
    public void An_empty_block_body_after_a_step_never_answers_before_the_block()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var editor = (MacroEditorViewModel)window.DataContext!;
            editor.AddStep(Step("control.delay",
                new StepParameter { Name = "ms", Kind = ActionParameterKind.Number, Value = "5" }));
            editor.AddStep(Step("control.repeat",
                new StepParameter { Name = "times", Kind = ActionParameterKind.Number, Value = "2" },
                new StepParameter { Name = "body", Kind = ActionParameterKind.Steps }));
            Dispatcher.UIThread.RunJobs();

            var list = window.FindControl<TreeView>("StepList")!;
            var hint = window.FindControl<TextBlock>("DropHintText")!;
            var head = Header(list, row => row.IsBlock);
            var top = head.TranslatePoint(new Point(0, head.Bounds.Height / 2), list)!.Value.Y;
            var bottom = head.TranslatePoint(new Point(0, head.Bounds.Height), list)!.Value.Y;
            var transfer = new ActionDragPayload(
                ActionPaletteCardKind.Action, "control.log").Transfer();

            var seen = new List<string>();
            for (var y = top; y < bottom; y += 1)
            {
                DragAt(list, DragDrop.DragOverEvent, transfer, new Point(60, y));
                seen.Add($"{y:0}:{hint.Text}");
            }

            Assert.DoesNotContain(seen, line => line.EndsWith("插入到这一块之前", StringComparison.Ordinal));

            Ui.CloseAndAnswer(window);
        });
    }
}
