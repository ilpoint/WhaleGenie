using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace WhaleGenie.Core.Expressions;

/// <summary>
/// A value as the macro file keeps it. A field that is only text is written as that text, so a
/// macro file still reads the way it always did where nothing is named; anything with a shape of
/// its own is written as an object tagged with the shape — <c>{"name": "count"}</c>,
/// <c>{"run": ["你好，", {"name": "who"}]}</c>, <c>{"call": "upper", "args": [{"name": "who"}]}</c>.
///
/// Nothing here writes a sign in front of a name, so there is no character in the file that has to
/// be escaped before it can be used as one.
/// </summary>
public static class FormulaJson
{
    private const string NameKey = "name";
    private const string RunKey = "run";
    private const string CallKey = "call";
    private const string ArgumentsKey = "args";
    private const string OperatorKey = "op";
    private const string LeftKey = "left";
    private const string RightKey = "right";
    private const string OperandKey = "of";
    private const string ListKey = "list";
    private const string DateKey = "date";

    /// <summary>The value as a JSON node, or null when there is nothing to write.</summary>
    public static JsonNode? Write(ExpressionNode? value)
    {
        switch (value)
        {
            case null:
            case TextNode { Text: "" }:
                return null;
            case TextNode text:
                return JsonValue.Create(text.Text);
            case DateNode date:
                return new JsonObject { [DateKey] = date.Text };
            case NumberNode number:
                return double.TryParse(number.Text, NumberStyles.Any, CultureInfo.InvariantCulture,
                    out var written)
                    ? JsonValue.Create(written)
                    : JsonValue.Create(number.Text);
            case ConstantNode constant:
                return constant.Word switch
                {
                    "true" => JsonValue.Create(true),
                    "false" => JsonValue.Create(false),
                    _ => null,
                };
            case VariableNode variable:
                return new JsonObject { [NameKey] = variable.Name };
            case ConcatNode run:
                return new JsonObject { [RunKey] = Array(run.Parts) };
            case ListNode list:
                return new JsonObject { [ListKey] = Array(list.Items) };
            case CallNode call:
                return new JsonObject { [CallKey] = call.Name, [ArgumentsKey] = Array(call.Arguments) };
            case UnaryNode unary:
                return new JsonObject
                {
                    [OperatorKey] = unary.Operator == "not" ? "not" : "-",
                    [OperandKey] = Write(unary.Operand),
                };
            case BinaryNode binary:
                return new JsonObject
                {
                    [OperatorKey] = binary.Operator,
                    [LeftKey] = Write(binary.Left),
                    [RightKey] = Write(binary.Right),
                };
            default:
                return null;
        }
    }

    private static JsonArray Array(IEnumerable<ExpressionNode> parts)
    {
        var array = new JsonArray();
        foreach (var part in parts)
        {
            array.Add(Write(part));
        }

        return array;
    }

    /// <summary>
    /// The value a node says. A string is text and nothing else — it is not read as a formula,
    /// which is what keeps a sentence containing a full stop from being taken for one — and the
    /// tagged objects are the shapes, read back the way they were written.
    /// </summary>
    public static ExpressionNode Read(JsonNode? node)
    {
        switch (node)
        {
            case null:
                return Formula.Nothing;
            case JsonObject written:
                return Object(written);
            case JsonValue value:
                if (value.TryGetValue<string>(out var text))
                {
                    return Formula.Text(text);
                }

                // A JSON number or truth value is the shape it is. Nothing is spelled out as a
                // line and parsed back: the file is not a place a formula is typed.
                if (value.TryGetValue<double>(out var number))
                {
                    return Formula.Number(number);
                }

                if (value.TryGetValue<bool>(out var flag))
                {
                    return Formula.Constant(flag ? "true" : "false");
                }

                return Formula.Text(value.ToJsonString());
            default:
                throw new InvalidDataException(
                    $"A value is written as a shape that is not one: {node.ToJsonString()}");
        }
    }

    private static ExpressionNode Object(JsonObject written)
    {
        if (written[NameKey] is JsonValue name)
        {
            if (!name.TryGetValue<string>(out var read))
            {
                throw Invalid(written, "a name that is not text");
            }

            return Formula.Name(read);
        }

        if (written[RunKey] is JsonArray run)
        {
            return Formula.Run(run.Select(Read));
        }

        if (written[DateKey] is JsonValue moment && moment.TryGetValue<string>(out var stamped))
        {
            return new DateNode(stamped);
        }

        if (written[ListKey] is JsonArray list)
        {
            return new ListNode([.. list.Select(Read)]);
        }

        if (written[CallKey] is JsonValue call && call.TryGetValue<string>(out var called))
        {
            var arguments = written[ArgumentsKey] is JsonArray given
                ? given.Select(Read).ToList()
                : [];
            return new CallNode(called, arguments);
        }

        if (written[OperatorKey] is JsonValue op && op.TryGetValue<string>(out var sign))
        {
            if (sign == "not")
            {
                return new UnaryNode("not", Read(written[OperandKey]));
            }

            if (written[OperandKey] is not null)
            {
                return new UnaryNode("-", Read(written[OperandKey]));
            }

            return new BinaryNode(sign, Read(written[LeftKey]), Read(written[RightKey]));
        }

        throw Invalid(written, "an object that is not a known shape");
    }

    private static InvalidDataException Invalid(JsonObject written, string reason)
        => new($"A value is written as {reason}: {written.ToJsonString()}");
}
