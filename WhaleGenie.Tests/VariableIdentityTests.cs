using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.Models;
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
        macro.Locals.Add(new MacroLocalVariable { Name = "count", Type = ActionReturnType.Number });
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
}
