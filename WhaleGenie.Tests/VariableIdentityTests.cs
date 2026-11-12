using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// A declaration has an identity, and a reference that names it carries that identity. The shape
/// is the only place it lives, so settling a document twice must not move anything and a rename
/// must follow the reference without looking at its spelling.
/// </summary>
public class VariableIdentityTests
{
    private static MacroItem Document()
    {
        var macro = new MacroItem { Name = "m" };
        macro.Locals.Add(new MacroLocalVariable { Name = "count", Type = ValueKind.Number });
        macro.Steps.Add(new MacroStep
        {
            Type = "control.setVariable",
            Meta = StepMeta.New,
            Parameters =
            [
                new StepParameter
                {
                    Name = "name",
                    Kind = ActionParameterKind.Text,
                    Written = Formula.Text("total"),
                },
                new StepParameter
                {
                    Name = "value",
                    Kind = ActionParameterKind.Text,
                    Written = Formula.Name("count"),
                },
            ],
        });
        return macro;
    }

    [Fact]
    public void Settling_gives_every_declaration_an_id()
    {
        var macro = Document();

        VariableIds.Settle(macro);

        Assert.True(VariableId.IsWellFormed(macro.Locals[0].Id));
        var declared = Formula.NameOf(macro.Steps[0].Parameters[0].Written);
        Assert.Equal("total", declared.Name);
        Assert.True(VariableId.IsWellFormed(declared.Id));
    }

    [Fact]
    public void A_reference_is_bound_to_the_only_declaration_of_its_name()
    {
        var macro = Document();

        VariableIds.Settle(macro);

        var value = Formula.NameOf(macro.Steps[0].Parameters[1].Written);
        Assert.Equal(macro.Locals[0].Id, value.Id);
    }

    [Fact]
    public void Settling_twice_changes_nothing()
    {
        var macro = Document();
        VariableIds.Settle(macro);
        var once = macro.ToJson().ToJsonString();

        VariableIds.Settle(macro);

        Assert.Equal(once, macro.ToJson().ToJsonString());
    }

    [Fact]
    public void Renaming_a_declaration_follows_the_reference()
    {
        var macro = Document();
        VariableIds.Settle(macro);
        macro.Locals[0].Name = "totalCount";

        VariableIds.RefreshNames(macro, []);

        Assert.Equal("totalCount", Formula.NameOf(macro.Steps[0].Parameters[1].Written).Name);
        Assert.Equal(macro.Locals[0].Id,
            Formula.NameOf(macro.Steps[0].Parameters[1].Written).Id);
    }

    [Fact]
    public void A_remap_points_every_reference_at_the_new_identity()
    {
        var macro = Document();
        VariableIds.Settle(macro);
        var old = macro.Locals[0].Id;

        VariableIds.Remap(macro, old, "a7k2m9");

        Assert.Equal("a7k2m9", Formula.NameOf(macro.Steps[0].Parameters[1].Written).Id);
    }

    /// <summary>
    /// A picker is where a reference is written. If the row it offers has forgotten the identity,
    /// the reference it puts in a field is bound by spelling, and the next rename leaves it behind.
    /// </summary>
    [Fact]
    public void A_name_a_step_writes_is_offered_with_its_identity()
    {
        Ui.Run(() =>
        {
            var step = new MacroStep
            {
                Type = "control.setVariable",
                Meta = StepMeta.New,
                Parameters =
                [
                    new StepParameter
                    {
                        Name = "name",
                        Kind = ActionParameterKind.Text,
                        Written = Formula.Name("rows", "a7k2m9"),
                    },
                ],
            };

            var row = Assert.Single(VariableChoices.For([step]), choice => choice.Name == "rows");

            Assert.Equal("a7k2m9", row.Id);
        });
    }

    /// <summary>
    /// Editing a global's name, type or default is not a new declaration: the identity has to
    /// survive the round trip through the editor, or every reference to it becomes stale.
    /// </summary>
    [Fact]
    public void Editing_a_global_declaration_keeps_its_identity()
    {
        var draft = new VariableDefinitionDraft(new VariableDefinition
        {
            Id = "a7k2m9",
            Name = "count",
            Scope = VariableScope.Global,
            Type = "number",
        })
        {
            Name = "total",
        };

        var written = draft.ToDefinition();

        Assert.Equal("a7k2m9", written.Id);
        Assert.Equal("total", written.Name);
    }
}
