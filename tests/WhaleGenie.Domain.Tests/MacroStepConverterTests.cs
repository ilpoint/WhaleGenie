using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Models;

namespace WhaleGenie.Domain.Tests;

/// <summary>
/// Turning the editor's steps into the tree a run walks. The editor keeps its rows open and
/// writes into them as the person types, so the conversion has to copy them: a run must not see a
/// cell change under it, and a later conversion must see the change.
/// </summary>
public class MacroStepConverterTests
{
    private static MacroStep Step(params StepParameter[] parameters)
        => new() { Type = "file.readCsv", Parameters = [.. parameters] };

    private static StepParameter Rows(StepParameterRow row)
        => new() { Name = "rows", Kind = ActionParameterKind.Region, Rows = [row] };

    [Fact]
    public void A_run_tree_keeps_the_cells_it_was_built_from()
    {
        var row = StepParameterRow.Of("x", Formula.Number(1));
        var tree = new[] { Step(Rows(row)) }.ToExecutable();

        // The editor writes into the row it still holds, as it does on every keystroke.
        row.Set("x", Formula.Number(2));

        var kept = (NumberNode)tree[0].Rows("rows")[0]["x"];
        Assert.Equal(1, kept.Value);

        // A second conversion is a second snapshot, and it sees what the editor holds now.
        var again = new[] { Step(Rows(row)) }.ToExecutable();
        Assert.Equal(2, ((NumberNode)again[0].Rows("rows")[0]["x"]).Value);
    }

    [Fact]
    public void A_row_that_gains_a_column_after_the_copy_does_not_gain_it_in_the_run()
    {
        var row = StepParameterRow.Of("x", Formula.Number(1));
        var tree = new[] { Step(Rows(row)) }.ToExecutable();

        row.Set("y", Formula.Text("later"));

        Assert.False(tree[0].Rows("rows")[0].ContainsKey("y"));
    }
}