using System.Linq;
using System.Text.Json.Nodes;
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
    public void A_number_written_as_an_expression_is_written_as_it_says()
    {
        // Reading a step back runs it past the action catalogue, which builds its icons with
        // Avalonia geometry, so the round trip belongs on the user-interface thread.
        Ui.Run(() =>
        {
            var step = Click("$sys.mouseX", "$sys.mouseY");

            var parameters = (JsonObject)step.ToJson()["params"]!;
            Assert.Equal("$sys.mouseX", parameters["x"]!.GetValue<string>());

            var read = MacroStep.FromJson(step.ToJson());
            Assert.Equal("$sys.mouseX", Value(read, "x"));
            Assert.Equal("$sys.mouseY", Value(read, "y"));
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

    /// <summary>A mouse click with the two halves of its place written as they say.</summary>
    private static MacroStep Click(string x, string y) => new()
    {
        Type = "input.mouseClick",
        Parameters =
        [
            new StepParameter { Name = "button", Kind = ActionParameterKind.Choice, Value = "left" },
            new StepParameter { Name = "x", Kind = ActionParameterKind.Number, Value = x },
            new StepParameter { Name = "y", Kind = ActionParameterKind.Number, Value = y },
        ],
    };
}
