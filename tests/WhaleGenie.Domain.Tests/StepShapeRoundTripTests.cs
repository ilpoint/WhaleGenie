using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Models;

namespace WhaleGenie.Domain.Tests;

/// <summary>
/// Every declared parameter, built as a shape, written to the file and read back: what a step is
/// has to be what the file keeps of it. The walk covers every action and every field shape,
/// including the row tables a variable or a formula can sit in — a file that reads the same line
/// is not the same value when the shape was a name.
/// </summary>
public class StepShapeRoundTripTests
{
    [Fact]
    public void Every_declared_parameter_comes_back_from_the_file_as_the_shape_it_was()
    {
        var failures = new List<string>();
        foreach (var definition in ActionCatalog.Definitions)
        {
            var step = new MacroStep { Type = definition.Key };
            foreach (var parameter in definition.Parameters)
            {
                step.Parameters.Add(Sample(parameter));
            }

            var written = step.ToJson();
            var read = MacroStep.FromJson(written);
            if (!JsonNode.DeepEquals(written, read.ToJson())
                || !SameParameters(step.Parameters, read.Parameters))
            {
                var difference = step.Parameters.Zip(read.Parameters)
                    .FirstOrDefault(pair => !SameParameter(pair.First, pair.Second));
                failures.Add(definition.Key + " [" + (difference.First is null
                    ? "file only"
                    : difference.First.Name + ": " + ShapeOf(difference.First.Written)
                      + " -> " + ShapeOf(difference.Second?.Written)) + "]");
            }
        }

        Assert.True(failures.Count == 0,
            "these actions did not come back as they were written: " + string.Join(", ", failures));
    }

    [Fact]
    public void A_window_rule_written_as_a_variable_comes_back_as_a_variable()
    {
        // A window rule is a record: a variable that holds one is still a name, not a literal rule
        // typed as JSON. The two are written the same way unless the reader asks which one it is.
        var step = new MacroStep
        {
            Type = "vision.findImage",
            Parameters =
            [
                new StepParameter
                {
                    Name = "window",
                    Kind = ActionParameterKind.WindowRule,
                    Written = Formula.Name("窗口"),
                },
            ],
        };

        var written = step.ToJson();
        var read = MacroStep.FromJson(written);

        Assert.True(JsonNode.DeepEquals(written, read.ToJson()), read.ToJson().ToJsonString());
        Assert.IsType<VariableNode>(
            read.Parameters.Single(parameter => parameter.Name == "window").Written);
    }

    /// <summary>
    /// The parameters as the step stores them, compared by shape rather than by the line the file
    /// shows. Two values can write the same JSON and be different shapes: a window rule held in a
    /// variable and one written out as a literal both end up as an object, and only the model says
    /// which one the next step will read.
    /// </summary>
    private static bool SameParameters(IReadOnlyList<StepParameter> left,
        IReadOnlyList<StepParameter> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        return left.Zip(right).All(pair => SameParameter(pair.First, pair.Second));
    }

    private static bool SameParameter(StepParameter left, StepParameter right)
        => left.Name == right.Name
           && left.Kind == right.Kind
           && left.Jitter == right.Jitter
           && SameShape(left.Written, right.Written)
           && left.Rows.Count == right.Rows.Count
           && left.Rows.Zip(right.Rows).All(pair => SameRow(pair.First, pair.Second))
           && SameSteps(left.Steps, right.Steps);

    private static bool SameRow(StepParameterRow left, StepParameterRow right)
        => left.Cells.Count == right.Cells.Count
           && left.Cells.All(cell => right.Cells.TryGetValue(cell.Key, out var other)
               && SameShape(cell.Value, other));

    private static bool SameSteps(IReadOnlyList<MacroStep> left, IReadOnlyList<MacroStep> right)
        => left.Count == right.Count
           && left.Zip(right).All(pair => pair.First.Type == pair.Second.Type
               && SameParameters(pair.First.Parameters, pair.Second.Parameters));

    private static string ShapeOf(ExpressionNode? value) => value switch
    {
        null => "null",
        TextNode text => "Text(" + text.Text + ")",
        NumberNode number => "Number(" + number.Text + ")",
        VariableNode name => "Name(" + name.Name + ")",
        ConcatNode => "Run",
        CallNode call => "Call(" + call.Name + ")",
        _ => value.GetType().Name,
    };

    private static bool SameShape(ExpressionNode? left, ExpressionNode? right) => (left, right) switch
    {
        (null, null) => true,
        (TextNode a, TextNode b) => a.Text == b.Text,
        (NumberNode a, NumberNode b) => a.Text == b.Text && a.Value.Equals(b.Value),
        (DateNode a, DateNode b) => a.Text == b.Text,
        (VariableNode a, VariableNode b) => a.Name == b.Name,
        (ConstantNode a, ConstantNode b) => a.Word == b.Word,
        (ConcatNode a, ConcatNode b) => SameList(a.Parts, b.Parts),
        (ListNode a, ListNode b) => SameList(a.Items, b.Items),
        (CallNode a, CallNode b) => a.Name == b.Name && SameList(a.Arguments, b.Arguments),
        (UnaryNode a, UnaryNode b) => a.Operator == b.Operator && SameShape(a.Operand, b.Operand),
        (BinaryNode a, BinaryNode b) => a.Operator == b.Operator
            && SameShape(a.Left, b.Left) && SameShape(a.Right, b.Right),
        (GroupNode a, GroupNode b) => SameShape(a.Inner, b.Inner),
        (HoleNode, HoleNode) => true,
        _ => false,
    };

    private static bool SameList(IReadOnlyList<ExpressionNode> left,
        IReadOnlyList<ExpressionNode> right)
        => left.Count == right.Count && left.Zip(right).All(pair => SameShape(pair.First, pair.Second));

    /// <summary>One parameter as the form writes it, by the kind of editor it opens.</summary>
    private static StepParameter Sample(ActionParameter parameter)
    {
        var kind = parameter.Kind;
        return kind switch
        {
            ActionParameterKind.Steps => new StepParameter
            {
                Name = parameter.Name,
                Kind = kind,
                Steps =
                [
                    new MacroStep
                    {
                        Type = "control.note",
                        Parameters =
                        [
                            new StepParameter
                            {
                                Name = "message",
                                Kind = ActionParameterKind.Text,
                                Written = Formula.Text("备注"),
                            },
                        ],
                    },
                ],
            },
            ActionParameterKind.Region => Rows(parameter.Name, kind,
                ("text", Formula.Name("区域")),
                ("x", Formula.Number(10)),
                ("y", Formula.Binary("+", Formula.Name("y"), Formula.Number(1))),
                ("width", Formula.Number(30)),
                ("height", Formula.Number(40))),
            ActionParameterKind.Images => Rows(parameter.Name, kind,
                ("image", Formula.Name("picture"))),
            ActionParameterKind.KeySequence => Rows(parameter.Name, kind,
                ("keys", Formula.Text("Ctrl+S")),
                ("holdMs", Formula.Number(20)),
                ("gapMs", Formula.Number(5))),
            ActionParameterKind.Returns => Rows(parameter.Name, kind,
                ("key", Formula.Text("match")),
                ("name", Formula.Name("命中"))),
            ActionParameterKind.Arguments => Rows(parameter.Name, kind,
                ("name", Formula.Text("arg")),
                ("value", Formula.Name("x")),
                ("given", Formula.Constant("true"))),
            ActionParameterKind.Narrow => Rows(parameter.Name, kind,
                ("how", Formula.Text("text")),
                ("value", Formula.Name("value"))),
            ActionParameterKind.ColourPoints => Rows(parameter.Name, kind,
                ("dx", Formula.Number(1)),
                ("dy", Formula.Number(2)),
                ("colour", Formula.Name("红")),
                ("mustNot", Formula.Constant("false")),
                ("tolerance", Formula.Number(10))),
            _ => new StepParameter
            {
                Name = parameter.Name,
                Kind = kind,
                Written = Shape(parameter),
            },
        };
    }

    private static StepParameter Rows(string name, ActionParameterKind kind,
        params (string Column, ExpressionNode Value)[] cells)
    {
        var row = new StepParameterRow();
        foreach (var (column, value) in cells)
        {
            row.Set(column, value);
        }

        return new StepParameter { Name = name, Kind = kind, Rows = [row] };
    }

    /// <summary>The shape a scalar field holds in the sample step.</summary>
    private static ExpressionNode Shape(ActionParameter parameter)
        => parameter.Kind switch
        {
            ActionParameterKind.Number => Formula.Number(10),
            ActionParameterKind.Bool => Formula.Constant("true"),
            ActionParameterKind.Choice => Formula.Text(parameter.Options.FirstOrDefault() ?? "x"),
            ActionParameterKind.Variable => Formula.Name("区域"),
            ActionParameterKind.WindowRule => Formula.Text("{\"title\":\"窗口\"}"),
            ActionParameterKind.Image => Formula.Name("picture"),
            ActionParameterKind.Point or ActionParameterKind.Rect
                or ActionParameterKind.Record => Formula.Name("位置"),
            ActionParameterKind.Expression => Formula.Binary("+", Formula.Name("x"),
                Formula.Number(1)),
            ActionParameterKind.Condition => Formula.Binary("==", Formula.Name("found"),
                Formula.Constant("true")),
            ActionParameterKind.Color => Formula.Text("#FF0000"),
            _ => Formula.Text("文本"),
        };
}
