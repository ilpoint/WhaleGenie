using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

public class VariableDropTests
{
    [Fact]
    public void A_boolean_variable_fills_a_condition_expression()
    {
        var parameter = ActionCatalog.Find("control.if")!
            .Parameters.Single(field => field.Name == "condition");
        var variable = new VariableDragPayload("found", ValueKind.Bool, []);

        var result = VariableDropResolver.Resolve(parameter, variable);

        Assert.Equal(VariableDropStatus.Direct, result.Status);
        Assert.Equal(Formula.Name("found"), result.Shape);
    }

    [Fact]
    public void A_record_fills_a_shape_that_uses_the_whole_record()
    {
        var parameter = ActionCatalog.Find("input.mouseClick")!
            .Parameters.Single(field => field.Name == "place");
        var variable = new VariableDragPayload("hit", ValueKind.Record,
        [
            new VariableDragField("x", ValueKind.Number),
            new VariableDragField("y", ValueKind.Number),
            new VariableDragField("basis", ValueKind.Text),
        ]);

        var result = VariableDropResolver.Resolve(parameter, variable);

        Assert.Equal(VariableDropStatus.Direct, result.Status);
        Assert.Equal(Formula.Name("hit"), result.Shape);
    }

    [Fact]
    public void A_record_with_two_matching_numbers_asks_which_field()
    {
        var parameter = ActionCatalog.Find("control.delay")!
            .Parameters.Single(field => field.Name == "ms");
        var variable = new VariableDragPayload("hit", ValueKind.Record,
        [
            new VariableDragField("x", ValueKind.Number),
            new VariableDragField("y", ValueKind.Number),
        ]);

        var result = VariableDropResolver.Resolve(parameter, variable);

        Assert.Equal(VariableDropStatus.ChooseField, result.Status);
        Assert.Equal(["x", "y"], result.Fields!.Select(field => field.Name));
    }

    [Fact]
    public void A_record_with_one_matching_number_fills_field_expression()
    {
        var parameter = ActionCatalog.Find("control.delay")!
            .Parameters.Single(field => field.Name == "ms");
        var variable = new VariableDragPayload("program", ValueKind.Record,
        [
            new VariableDragField("memoryMb", ValueKind.Number),
            new VariableDragField("name", ValueKind.Text),
        ]);

        var result = VariableDropResolver.Resolve(parameter, variable);

        Assert.Equal(VariableDropStatus.Direct, result.Status);
        AssertSameShape(
            Formula.Call("field", Formula.Name("program"), Formula.Text("memoryMb")),
            result.Shape);
    }

    [Fact]
    public void A_variable_drag_keeps_its_shape_and_fields()
    {
        var original = new VariableDragPayload("hit", ValueKind.Record,
        [
            new VariableDragField("x", ValueKind.Number),
            new VariableDragField("found", ValueKind.Bool),
        ]);

        var transfer = original.Transfer();
        var read = VariableDragPayload.Read(transfer);

        Assert.NotNull(read);
        Assert.Same(original, read);
        Assert.Equal(DataFormatKind.InProcess, VariableDragPayload.InProcessFormat.Kind);
        Assert.Contains(VariableDragPayload.InProcessFormat,
            transfer.Items.Single().Formats);
        Assert.Equal("hit", read.Name);
        Assert.Equal(ValueKind.Record, read.Type);
        Assert.Equal(["x", "found"], read.Fields.Select(field => field.Name));
    }

    [Fact]
    public void The_macro_editor_right_panel_has_a_variable_center_above_its_settings()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            macro.Steps.Add(new MacroStep
            {
                Type = "control.setVariable",
                Parameters =
                [
                    new StepParameter { Name = "name", Kind = ActionParameterKind.Variable, Written = Fields.Shape("answer") },
                    new StepParameter { Name = "scope", Kind = ActionParameterKind.Choice, Written = Fields.Shape("local") },
                    new StepParameter { Name = "value", Kind = ActionParameterKind.Expression, Written = Formula.Number(1) },
                ],
            });
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var editor = (MacroEditorViewModel)window.DataContext!;
            Assert.NotNull(window.FindControl<Border>("VariableCenterPanel"));
            Assert.NotNull(window.FindControl<VariableWall>("VariableCenterWall"));
            Assert.NotNull(window.FindControl<Border>("MacroSettingsDrawer"));
            Assert.Equal(3, window.GetVisualDescendants().OfType<GridSplitter>().Count());
            Assert.Contains(editor.VariableCenter.Wall.Shown,
                choice => choice.Name == "answer");

            window.Close();
        });
    }

    [Fact]
    public void The_flow_dock_is_grouped_instead_of_one_flat_list()
    {
        var palette = new ActionPaletteViewModel();
        palette.SelectDock(ActionPaletteDock.Flow);

        Assert.Equal(
            [
                Strings.Get("Palette.Flow.Branch"), Strings.Get("Palette.Flow.Jump"),
                Strings.Get("Palette.Flow.Waiting"), Strings.Get("Palette.Flow.Data"),
                Strings.Get("Palette.Flow.Notes"),
            ],
            palette.Sections.Select(section => section.Title));
        Assert.Contains(palette.Sections[0].Cards, card => card.Key == "control.if");
        Assert.Contains(palette.Sections[2].Cards, card => card.Key == "control.waitUntil");
    }

    [Fact]
    public void A_name_field_wants_the_word_not_a_reference()
    {
        var parameter = ActionCatalog.Find("control.setVariable")!
            .Parameters.Single(field => field.Name == "name");
        var variable = new VariableDragPayload("answer", ValueKind.Text, []);

        var result = VariableDropResolver.Resolve(parameter, variable);

        Assert.Equal(VariableDropStatus.Direct, result.Status);
        Assert.Equal(Formula.Text("answer"), result.Shape);
    }

    [Fact]
    public void A_name_chosen_for_a_cell_keeps_the_identity_of_its_declaration()
    {
        var form = new ValuePickerViewModel(Formula.Nothing, [], ActionValueForm.Name);
        ValuePick? answer = null;
        form.CloseRequested += pick => answer = pick;

        form.Choose(new VariableChoice
        {
            Name = "count",
            Scope = VariableScope.Local,
        });

        var handed = Assert.IsType<ValuePick>(answer).Value;
        Assert.Equal("count", Assert.IsType<VariableNode>(handed).Name);
    }

    /// <summary>A call holds a list, so record equality would compare that list by identity.</summary>
    private static void AssertSameShape(ExpressionNode expected, ExpressionNode? actual)
    {
        if (expected is CallNode call)
        {
            var got = Assert.IsType<CallNode>(actual);
            Assert.Equal(call.Name, got.Name);
            Assert.Equal(call.Arguments.Count, got.Arguments.Count);
            for (var index = 0; index < call.Arguments.Count; index++)
            {
                AssertSameShape(call.Arguments[index], got.Arguments[index]);
            }

            return;
        }

        Assert.Equal(expected, actual);
    }
}
