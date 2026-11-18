using System.Linq;
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
            VariableText.Rename("before $old ${old} ${$old + 1} $$old", "old", "new"));
        Assert.Equal("$new + $$123", Expression.RenameVariable("$123 + $$123", "123", "new"));
        Assert.Equal("before $new", VariableText.Rename("before $123", "123", "new"));
    }

    [Fact]
    public void Step_rename_updates_target_fields_and_references_but_not_record_fields()
    {
        var step = new MacroStep
        {
            Type = "control.setVariable",
            Parameters =
            [
                new StepParameter { Name = "name", Kind = ActionParameterKind.Text, Value = "old" },
                new StepParameter { Name = "scope", Kind = ActionParameterKind.Text, Value = "global" },
                new StepParameter
                {
                    Name = "value",
                    Kind = ActionParameterKind.Expression,
                    Value = "field($record, \"old\") + $old",
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
                new StepParameter { Name = "name", Kind = ActionParameterKind.Text, Value = "old" },
                new StepParameter { Name = "scope", Kind = ActionParameterKind.Text, Value = "local" },
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
                    Value = "WhaleGenie.GetProperty(\"old\")",
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
                new StepParameter { Name = "name", Kind = ActionParameterKind.Text, Value = "old" },
                new StepParameter { Name = "scope", Kind = ActionParameterKind.Text, Value = "local" },
                new StepParameter { Name = "value", Kind = ActionParameterKind.Expression, Value = "7" },
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
                    Value = "$old + 1",
                },
            ],
        });

        Assert.True(editor.RenameLocalVariable("old", "new", out var problem), problem);
        Assert.Equal("new", editor.Steps[0].Text("name"));
        Assert.Equal("$new + 1", editor.Steps[1].Text("message"));
    }
}
