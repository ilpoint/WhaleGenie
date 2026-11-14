using System.Linq;
using System.Text.Json.Nodes;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Models;

namespace WhaleGenie.Tests;

/// <summary>
/// What a step comes to in the file it is written to. The file is the only copy of the work once
/// the program has closed, so a value written as something other than what it says is a step that
/// does something other than what it said — and nothing on screen says so, because what comes back
/// is a valid value of the right kind.
/// </summary>
public class StepFileTests
{
    /// <summary>
    /// A number field may hold an expression — <c>$sys.mouseX</c>, where the pointer is when the
    /// step runs — and it has to be written as the expression it is. Written as a zero, because it
    /// is not digits, it saves a step that clicks the corner of the screen the moment the macro was
    /// written, which is the opposite of clicking wherever the user left the mouse.
    /// </summary>
    [Fact]
    public void A_number_written_as_an_expression_is_written_as_the_name_it_reads()
    {
        // Reading a step back runs it past the action catalogue, which builds its icons with
        // Avalonia geometry, so the round trip belongs on the user-interface thread.
        Ui.Run(() =>
        {
            var step = ClickShape(Formula.Name("sys.mouseX"), Formula.Name("sys.mouseY"));

            // The file says which name it reads rather than the line that reads it: a line would
            // be a second language for the same fact, and the reader of a file would have to know
            // it to tell a name from a piece of text.
            var parameters = (JsonObject)step.ToJson()["params"]!;
            Assert.Equal("sys.mouseX", parameters["x"]!["name"]!.GetValue<string>());

            var read = MacroStep.FromJson(step.ToJson());
            Assert.Equal("sys.mouseX", Value(read, "x"));
            Assert.Equal("sys.mouseY", Value(read, "y"));
        });
    }

    [Fact]
    public void A_number_is_still_written_as_a_number()
    {
        Ui.Run(() =>
        {
            var step = Click("100", "200");

            var parameters = (JsonObject)step.ToJson()["params"]!;
            Assert.Equal(100m, parameters["x"]!.GetValue<decimal>());
        });
    }

    /// <summary>
    /// A number nobody answered is the one case that keeps reading as zero, which is what the engine
    /// has always made of it: only a file written by hand can hold one, because the editor leaves an
    /// unanswered number out of the step.
    /// </summary>
    [Fact]
    public void A_number_nobody_answered_is_still_written_as_zero()
    {
        Ui.Run(() =>
        {
            var step = Click(string.Empty, string.Empty);

            var parameters = (JsonObject)step.ToJson()["params"]!;
            Assert.Equal(0m, parameters["x"]!.GetValue<decimal>());
        });
    }

    private static string Value(MacroStep step, string name)
        => step.Parameters.First(parameter => parameter.Name == name).Value;

    [Fact]
    public void A_row_cell_is_a_shape_and_only_a_typed_line_becomes_one()
    {
        // Every column is a shape. A line a person typed is the one place a shape is read from,
        // and it is read as the words it says — a name is a piece put in by the value window.
        var row = StepParameterRow.Of("value", Formula.Name("count", "a7k2m9"));
        row.Set("given", Formula.Constant("true"));
        row.Set("note", Formula.Run([
            Formula.Text("共 "), Formula.Name("count"), Formula.Text(" 件")]));
        row.SetLine("typed", "共 count 件");

        Assert.Equal(Formula.Name("count", "a7k2m9"), row.Cell("value"));
        Assert.Equal(Formula.Constant("true"), row.Cell("given"));
        var note = Assert.IsType<ConcatNode>(row.Cell("note"));
        Assert.Equal(
            new ExpressionNode[] { Formula.Text("共 "), Formula.Name("count"), Formula.Text(" 件") },
            note.Parts);
        Assert.Equal(Formula.Text("共 count 件"), row.Cell("typed"));

        // What goes into the file is the shape, not a line someone would have to read back.
        var parameter = new StepParameter
        {
            Name = "arguments",
            Kind = ActionParameterKind.Arguments,
            Rows = [row],
        };
        var written = Assert.IsType<JsonArray>(parameter.ToJson());
        var cell = Assert.IsType<JsonObject>(written[0]);
        var value = Assert.IsType<JsonObject>(cell["value"]);
        Assert.Equal("count", Assert.IsAssignableFrom<JsonValue>(value["name"]).GetValue<string>());
        Assert.Equal("a7k2m9", Assert.IsAssignableFrom<JsonValue>(value["id"]).GetValue<string>());
        Assert.True(Assert.IsAssignableFrom<JsonValue>(cell["given"]).GetValue<bool>());

        var restored = StepParameter.FromJson("arguments", written,
            ActionCatalog.Find("control.runMacro"));
        Assert.Equal(Formula.Name("count", "a7k2m9"), restored.Rows[0].Cell("value"));
        Assert.Equal(Formula.Constant("true"), restored.Rows[0].Cell("given"));
    }

    /// <summary>A mouse click with the two halves of its place written as they say.</summary>
    private static MacroStep ClickShape(ExpressionNode x, ExpressionNode y) => new()
    {
        Type = "input.mouseClick",
        Parameters =
        [
            new StepParameter { Name = "button", Kind = ActionParameterKind.Choice, Written = Formula.Text("left") },
            new StepParameter { Name = "x", Kind = ActionParameterKind.Number, Written = x },
            new StepParameter { Name = "y", Kind = ActionParameterKind.Number, Written = y },
        ],
    };

    private static MacroStep Click(string x, string y) => new()
    {
        Type = "input.mouseClick",
        Parameters =
        [
            new StepParameter { Name = "button", Kind = ActionParameterKind.Choice, Written = Fields.Shape("left") },
            new StepParameter { Name = "x", Kind = ActionParameterKind.Number, Written = Fields.Shape(x) },
            new StepParameter { Name = "y", Kind = ActionParameterKind.Number, Written = Fields.Shape(y) },
        ],
    };
}
