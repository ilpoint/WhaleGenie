using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Execution;
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
        var variable = new VariableDragPayload("found", "bool", []);

        var result = VariableDropResolver.Resolve(parameter, variable);

        Assert.Equal(VariableDropStatus.Direct, result.Status);
        Assert.Equal("$found", result.Text);
    }

    [Fact]
    public void A_record_fills_a_shape_that_uses_the_whole_record()
    {
        var parameter = ActionCatalog.Find("input.mouseClick")!
            .Parameters.Single(field => field.Name == "place");
        var variable = new VariableDragPayload("hit", "record",
        [
            new VariableDragField("x", ActionReturnType.Number),
            new VariableDragField("y", ActionReturnType.Number),
            new VariableDragField("basis", ActionReturnType.Text),
        ]);

        var result = VariableDropResolver.Resolve(parameter, variable);

        Assert.Equal(VariableDropStatus.Direct, result.Status);
        Assert.Equal("$hit", result.Text);
    }

    [Fact]
    public void A_record_with_two_matching_numbers_asks_which_field()
    {
        var parameter = ActionCatalog.Find("control.delay")!
            .Parameters.Single(field => field.Name == "ms");
        var variable = new VariableDragPayload("hit", "record",
        [
            new VariableDragField("x", ActionReturnType.Number),
            new VariableDragField("y", ActionReturnType.Number),
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
        var variable = new VariableDragPayload("program", "record",
        [
            new VariableDragField("memoryMb", ActionReturnType.Number),
            new VariableDragField("name", ActionReturnType.Text),
        ]);

        var result = VariableDropResolver.Resolve(parameter, variable);

        Assert.Equal(VariableDropStatus.Direct, result.Status);
        Assert.Equal("field($program, \"memoryMb\")", result.Text);
    }

    [Fact]
    public void A_variable_drag_keeps_its_shape_and_fields()
    {
        var original = new VariableDragPayload("hit", "record",
        [
            new VariableDragField("x", ActionReturnType.Number),
            new VariableDragField("found", ActionReturnType.Flag),
        ]);

        var transfer = original.Transfer();
        var read = VariableDragPayload.Read(transfer);

        Assert.NotNull(read);
        Assert.Same(original, read);
        Assert.Equal(DataFormatKind.InProcess, VariableDragPayload.InProcessFormat.Kind);
        Assert.Contains(VariableDragPayload.InProcessFormat,
            transfer.Items.Single().Formats);
        Assert.Equal("hit", read.Name);
        Assert.Equal("record", read.Type);
        Assert.Equal(["x", "found"], read.Fields.Select(field => field.Name));
    }

    [Fact]
    public void A_field_replaces_a_whole_value_but_inserts_into_text()
    {
        Ui.Run(() =>
        {
            var conditional = new AddActionViewModel(ActionCatalog.Definitions, [], []);
            conditional.SelectAction("control.if");
            var condition = conditional.Parameters.Single(parameter =>
                parameter.Definition.Name == "condition");
            condition.ApplyVariableDrop("$ok");
            Assert.Equal("$ok", condition.Text);

            conditional.SelectAction("control.note");
            var message = conditional.Parameters.Single(parameter =>
                parameter.Definition.Name == "note");
            message.Text = "before after";
            message.ApplyVariableDrop("$name", 7);
            Assert.Equal("before $nameafter", message.Text);
        });
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
                    new StepParameter { Name = "name", Kind = ActionParameterKind.Variable, Value = "answer" },
                    new StepParameter { Name = "scope", Kind = ActionParameterKind.Choice, Value = "local" },
                    new StepParameter { Name = "value", Kind = ActionParameterKind.Expression, Value = "1" },
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
}
