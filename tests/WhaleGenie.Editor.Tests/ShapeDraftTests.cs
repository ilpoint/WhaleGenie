using System.Linq;
using System.Text.Json.Nodes;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Tests;

/// <summary>
/// The form's rows and the step's rows are the same values. A row that is filled from a saved step
/// and written back without being touched has to come out as it went in: the line the form shows
/// is a rendering, and a variable or a formula that went through it would come back as text.
/// </summary>
public class ShapeDraftTests
{
    [Fact]
    public void A_region_row_keeps_the_variable_it_was_written_with()
    {
        // A row says one thing: four numbers, or the name of one variable. Both at once is not a
        // shape the editor ever writes, so the name is the row.
        var row = new StepParameterRow();
        row.Set("text", Formula.Name("区域"));

        var written = RegionRowViewModel.From(row).ToRow();

        SameCells(row, written);
    }

    [Fact]
    public void A_region_row_written_as_four_numbers_keeps_them_as_numbers_and_formulas()
    {
        var row = new StepParameterRow();
        row.Set("x", Formula.Binary("+", Formula.Name("x"), Formula.Number(10)));
        row.Set("y", Formula.Number(20));
        row.Set(FieldNames.Width, Formula.Number(30));
        row.Set(FieldNames.Height, Formula.Number(40));

        var written = RegionRowViewModel.From(row).ToRow();

        SameCells(row, written);
    }

    [Fact]
    public void A_rectangle_written_into_the_one_name_box_stays_text()
    {
        var row = new StepParameterRow();
        row.Set("text", Formula.Text("10,20,30,40"));

        var written = RegionRowViewModel.From(row).ToRow();

        SameCells(row, written);
    }

    [Fact]
    public void A_text_box_that_is_not_a_rectangle_and_not_a_name_is_text()
    {
        // The one-name box writes text for anything that is not a rectangle, and a name for a word
        // that could be one. A path is neither, so it stays the text it was typed as.
        var row = new StepParameterRow();
        row.Set("text", Formula.Text("C:\\shot.png"));

        var written = RegionRowViewModel.From(row).ToRow();

        SameCells(row, written);
    }

    [Fact]
    public void A_call_argument_keeps_the_variable_it_passes()
    {
        var row = new StepParameterRow();
        row.Set(FieldNames.Name, Formula.Text("arg"));
        row.Set(FieldNames.Value, Formula.Name("x"));
        row.Set(FieldNames.Given, Formula.Constant("true"));

        var argument = new CallArgumentRowViewModel("arg", string.Empty, required: false,
            declared: true);
        argument.Load(row.Cell(FieldNames.Value), given: true);

        SameCells(row, argument.ToRow()!);
    }

    private static void SameCells(StepParameterRow expected, StepParameterRow actual)
    {
        Assert.Equal(
            expected.Cells.Keys.Order(StringComparer.OrdinalIgnoreCase),
            actual.Cells.Keys.Order(StringComparer.OrdinalIgnoreCase));
        foreach (var (column, value) in expected.Cells)
        {
            Assert.True(JsonNode.DeepEquals(FormulaJson.Write(value),
                    FormulaJson.Write(actual.Cell(column))),
                column + ": " + Formula.Speak(value));
        }
    }
}
