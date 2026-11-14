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
    public void Step_rename_updates_target_fields_and_references_but_not_record_fields()
    {
        var step = new MacroStep
        {
            Type = "control.setVariable",
            Parameters =
            [
                new StepParameter { Name = "name", Kind = ActionParameterKind.Text, Written = Fields.Shape("old") },
                new StepParameter { Name = "scope", Kind = ActionParameterKind.Text, Written = Fields.Shape("global") },
                new StepParameter
                {
                    Name = "value",
                    Kind = ActionParameterKind.Expression,
                    Written = Expression.Read("field($record, \"old\") + $old"),
                },
            ],
        };

        VariableRename.RenameSteps([step], "old", "new", global: true);

        Assert.Equal("new", step.Text("name"));
        Assert.Equal("field(「record」, old) + 「new」", step.Text("value"));
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
                new StepParameter { Name = "name", Kind = ActionParameterKind.Text, Written = Fields.Shape("old") },
                new StepParameter { Name = "scope", Kind = ActionParameterKind.Text, Written = Fields.Shape("local") },
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
                    Written = Fields.Shape("WhaleGenie.GetProperty(\"old\")"),
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
                new StepParameter { Name = "name", Kind = ActionParameterKind.Text, Written = Fields.Shape("old") },
                new StepParameter { Name = "scope", Kind = ActionParameterKind.Text, Written = Fields.Shape("local") },
                new StepParameter { Name = "value", Kind = ActionParameterKind.Expression, Written = Formula.Number(7) },
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
                    Written = Expression.Read("$old + 1"),
                },
            ],
        });

        Assert.True(editor.RenameLocalVariable("old", "new", out var problem), problem);
        Assert.Equal("new", editor.Steps[0].Text("name"));
        Assert.Equal("「new」 + 1", editor.Steps[1].Text("message"));
    }

    [Fact]
    public void A_global_rename_updates_the_shapes_a_declaration_keeps()
    {
        var macro = new MacroItem { Name = "declaring" };
        macro.Parameters.Add(new MacroParameter
        {
            Name = "count",
            Required = false,
            Default = Formula.Binary("+", Formula.Name("old"), Formula.Number(1)),
        });
        macro.Locals.Add(new MacroLocalVariable
        {
            Name = "total",
            Initial = Formula.Run([Formula.Text("共 "), Formula.Name("old"), Formula.Text(" 件")]),
        });

        VariableRename.RenameGlobal([macro], "old", "new");

        Assert.Equal(Formula.Binary("+", Formula.Name("new"), Formula.Number(1)),
            macro.Parameters[0].Default);
        var run = Assert.IsType<ConcatNode>(macro.Locals[0].Initial);
        Assert.Equal(new ExpressionNode[] { Formula.Text("共 "), Formula.Name("new"), Formula.Text(" 件") },
            run.Parts);
    }
}
