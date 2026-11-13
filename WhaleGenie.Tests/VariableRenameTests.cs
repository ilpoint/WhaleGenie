using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Tests;

/// <summary>Variable rename is token-aware and leaves strings, record fields and scripts alone.</summary>
public class VariableRenameTests
{
    [Fact]
    public void Expressions_and_text_interpolation_rename_only_variable_tokens()
    {
        Assert.Equal("$new + $other + \"$old\"",
            Expression.RenameVariable("$old + $other + \"$old\"", "old", "new"));
        Assert.Equal("before $new ${new} ${$new + 1} $$old",
            FormulaText.Rename("before $old ${old} ${$old + 1} $$old",
                ActionValueForm.Text, "old", "new"));
        Assert.Equal("$new + $$123", Expression.RenameVariable("$123 + $$123", "123", "new"));
        Assert.Equal("before $new",
            FormulaText.Rename("before $123", ActionValueForm.Text, "123", "new"));
    }

    [Fact]
    public void Step_rename_updates_target_fields_and_references_but_not_record_fields()
    {
        var step = new MacroStep
        {
            Type = "control.setVariable",
            Parameters =
            [
                new StepParameter { Name = "name", Kind = ActionParameterKind.Text, Written = Formula.FromLine("old") },
                new StepParameter { Name = "scope", Kind = ActionParameterKind.Text, Written = Formula.FromLine("global") },
                new StepParameter
                {
                    Name = "value",
                    Kind = ActionParameterKind.Expression,
                    Written = Formula.FromLine("field($record, \"old\") + $old"),
                },
            ],
        };

        VariableRename.RenameSteps([step], "old", "new", global: true);

        Assert.Equal("new", step.Text("name"));
        Assert.Equal("field($record, \"old\") + $new", step.Text("value"));
    }

    [Fact]
    public void A_shadowing_local_blocks_global_rename_and_scripts_are_reported()
    {
        var macro = new MacroItem { Name = "scripted" };
        macro.Steps.Add(new MacroStep
        {
            Type = "control.setVariable",
            Parameters =
            [
                new StepParameter { Name = "name", Kind = ActionParameterKind.Text, Written = Formula.FromLine("old") },
                new StepParameter { Name = "scope", Kind = ActionParameterKind.Text, Written = Formula.FromLine("local") },
            ],
        });
        macro.Steps.Add(new MacroStep
        {
            Type = "script.python",
            Parameters =
            [
                new StepParameter
                {
                    Name = "script",
                    Kind = ActionParameterKind.Text,
                    Written = Formula.FromLine("WhaleGenie.GetProperty(\"old\")"),
                },
            ],
        });

        Assert.Equal("old", VariableRename.GlobalConflict([macro], "old"));
        Assert.Equal(["scripted"], VariableRename.ScriptUsers([macro], "old"));
    }

    [Fact]
    public void A_local_rename_updates_its_writer_and_reader()
    {
        var editor = new MacroEditorViewModel();
        editor.Steps.Add(new MacroStep
        {
            Type = "control.setVariable",
            Parameters =
            [
                new StepParameter { Name = "name", Kind = ActionParameterKind.Text, Written = Formula.FromLine("old") },
                new StepParameter { Name = "scope", Kind = ActionParameterKind.Text, Written = Formula.FromLine("local") },
                new StepParameter { Name = "value", Kind = ActionParameterKind.Expression, Written = Formula.FromLine("7") },
            ],
        });
        editor.Steps.Add(new MacroStep
        {
            Type = "control.log",
            Parameters =
            [
                new StepParameter
                {
                    Name = "message",
                    Kind = ActionParameterKind.Expression,
                    Written = Formula.FromLine("$old + 1"),
                },
            ],
        });

        Assert.True(editor.RenameLocalVariable("old", "new", out var problem), problem);
        Assert.Equal("new", editor.Steps[0].Text("name"));
        Assert.Equal("$new + 1", editor.Steps[1].Text("message"));
    }
}
