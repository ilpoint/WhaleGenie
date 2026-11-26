using System.Linq;
using System.Text.Json.Nodes;
using WhaleGenie.Contracts;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Editing;
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
    public void An_empty_call_argument_is_left_out()
    {
        var argument = new CallArgumentRowViewModel("arg", string.Empty, required: false,
            declared: true);

        Assert.Null(argument.ToRow());
    }

    [Fact]
    public void An_empty_argument_passed_on_purpose_is_written_without_a_value()
    {
        var argument = new CallArgumentRowViewModel("arg", string.Empty, required: false,
            declared: true);
        argument.Load(Formula.Nothing, given: true);

        var row = argument.ToRow();

        Assert.NotNull(row);
        Assert.Equal("arg", row!.Text(FieldNames.Name));
        Assert.Equal("true", Formula.Word(row.Cell(FieldNames.Given)));
        Assert.False(row.Cells.ContainsKey(FieldNames.Value));
    }

    [Fact]
    public void A_region_line_is_a_rectangle_only_when_every_place_is_four_numbers()
    {
        Assert.True(RowShapes.LooksLikeRectangle("10,20,30,40;\n"));
        Assert.False(RowShapes.LooksLikeRectangle(""));
        Assert.False(RowShapes.LooksLikeRectangle("10,20,30,40;bad"));
        Assert.False(RowShapes.LooksLikeRectangle("1,2,3,x"));
        Assert.False(RowShapes.LooksLikeRectangle("1,2,3,4,5"));
    }

    [Fact]
    public void A_parts_cell_keeps_a_name_only_when_it_names_something()
    {
        var shape = EditorValueParts.ShapeOf(
            [new EditorValuePartSnapshot("name", "literally text", string.Empty)]);

        Assert.Equal(Formula.Text("literally text"), shape);
        Assert.True(Formula.IsNothing(EditorValueParts.ShapeOf([])));
        Assert.True(Formula.IsNothing(EditorValueParts.ShapeOf(null)));

        var fallback = EditorValueParts.Of(
            Formula.Run([Formula.Text("a"), Formula.Number("1", 1d)])).Last();
        Assert.Equal("text", fallback.Kind);
        Assert.Equal(string.Empty, fallback.Name);
    }

    [Fact]
    public void An_empty_region_row_reads_as_four_empty_cells()
    {
        var reading = RowShapes.ReadRegion(new StepParameterRow());

        Assert.Equal(string.Empty, reading.Variable);
        Assert.True(Formula.IsNothing(reading.X));
        Assert.True(Formula.IsNothing(reading.Y));
        Assert.True(Formula.IsNothing(reading.Width));
        Assert.True(Formula.IsNothing(reading.Height));
        Assert.True(Formula.IsNothing(RowShapes.RegionText("   ")));
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
