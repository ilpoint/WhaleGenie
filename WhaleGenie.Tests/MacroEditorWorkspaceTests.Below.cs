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

// A drop below a block, and the drop over cases that is refused before the form opens.
public partial class MacroEditorWorkspaceTests
{
    [Fact]
    public void A_drop_below_an_expanded_block_appends_to_the_top_level()
    {
        Ui.Run(() =>
        {
            var child = Step("control.log");
            var loop = Step("control.repeat",
                new StepParameter
                {
                    Name = "times",
                    Kind = ActionParameterKind.Number,
                    Written = Formula.FromLine("2"),
                },
                new StepParameter
                {
                    Name = "body",
                    Kind = ActionParameterKind.Steps,
                    Steps = [child],
                });
            loop.IsExpanded = true;
            var macro = new MacroItem { Name = "probe" };
            macro.Steps.Add(loop);

            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var editor = (MacroEditorViewModel)window.DataContext!;
            var list = window.FindControl<TreeView>("StepList")!;

            // Below the whole tree is past everything, which is the top level.
            var point = new Point(80, list.Bounds.Height - 4);
            var transfer = new ActionDragPayload(
                ActionPaletteCardKind.Action, "control.delay").Transfer();

            DragAt(list, DragDrop.DropEvent, transfer, point);
            Dispatcher.UIThread.RunJobs();
            var action = Assert.Single(window.OwnedWindows.OfType<ActionEditorWindow>());
            var form = (AddActionViewModel)action.DataContext!;
            form.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(["control.repeat", "control.delay"],
                editor.Steps.Select(step => step.Type));
            Assert.Equal("control.log",
                Assert.Single(loop.StepLists.Single().Steps).Type);

            Ui.CloseAndAnswer(window);
        });
    }

    [Fact]
    public void Dragging_an_ordinary_action_over_cases_is_refused_before_the_form_opens()
    {
        Ui.Run(() =>
        {
            var switchStep = Step("control.switch",
                new StepParameter
                {
                    Name = "cases",
                    Kind = ActionParameterKind.Steps,
                },
                new StepParameter
                {
                    Name = "otherwise",
                    Kind = ActionParameterKind.Steps,
                });
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var editor = (MacroEditorViewModel)window.DataContext!;
            editor.AddStep(switchStep);
            Dispatcher.UIThread.RunJobs();
            var list = window.FindControl<TreeView>("StepList")!;
            var marker = window.FindControl<Border>("DropMarker")!;
            var hint = window.FindControl<TextBlock>("DropHintText")!;
            // The branch table of an empty switch is the line that adds the first branch, so that
            // is the place a dragged action lands on — and a land there is a branch, nothing else.
            var casesHead = Header(list, row => row.IsAddRow);
            var point = casesHead.TranslatePoint(
                new Point(casesHead.Bounds.Width / 2, casesHead.Bounds.Height / 2), list)!.Value;
            var transfer = new ActionDragPayload(
                ActionPaletteCardKind.Action, "control.log").Transfer();

            var over = DragAt(list, DragDrop.DragOverEvent, transfer, point);
            Assert.Equal(DragDropEffects.None, over.DragEffects);
            Assert.True(marker.IsVisible);
            Assert.Contains("Rejected", marker.Classes);
            Assert.Equal(Strings.Format("Editor.PlaceTakesOnly",
                ActionCatalog.Find("control.case")!.LocalName), hint.Text);

            DragAt(list, DragDrop.DropEvent, transfer, point);
            Dispatcher.UIThread.RunJobs();

            Assert.Empty(window.OwnedWindows.OfType<ActionEditorWindow>());
            Assert.Empty(switchStep.StepLists.Single(parameter => parameter.Name == "cases").Steps);
            Assert.True(editor.HasNotice);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
                text => text.IsEffectivelyVisible && text.Text == editor.Notice);
            Ui.CloseAndAnswer(window);
        });
    }

    /// <summary>One row's own header, which is where a drop seam is measured.</summary>
    private static Panel Header(TreeView list, Func<StepRow, bool> match)
        => list.GetVisualDescendants().OfType<Panel>()
            .Single(panel => panel.Name == "RowHeader"
                && panel.DataContext is StepRow row && match(row));

    private static MacroStep Step(string type, params StepParameter[] parameters)
        => new() { Type = type, Parameters = [.. parameters] };

    private static MacroStep Call(string macro) => Step("control.runMacro",
        new StepParameter
        {
            Name = "macro",
            Kind = ActionParameterKind.Macro,
            Written = Formula.FromLine(macro),
        });

    private static DragEventArgs Drag(Control target, RoutedEvent<DragEventArgs> what,
        DataTransfer transfer)
        => DragAt(target, what, transfer, new Point(8, 8));

    private static DragEventArgs DragAt(Control target, RoutedEvent<DragEventArgs> what,
        DataTransfer transfer, Point point)
    {
        var args = new DragEventArgs(what, transfer, target, point,
            KeyModifiers.None);
        target.RaiseEvent(args);
        return args;
    }
}
