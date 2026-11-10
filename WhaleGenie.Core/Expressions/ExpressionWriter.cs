using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;


namespace WhaleGenie.Core.Expressions;

// Writing a tree back as text, with the brackets it needs.
/// <summary>
/// Writes a tree back out as text. Brackets are put in only where the tree would read differently
/// without them, which is what lets the editor own the shape of a formula: the user moves boxes
/// around and this decides where the brackets land.
/// </summary>
internal static class ExpressionWriter
{
    /// <summary>What binds loosest, counting up to the pieces that bind nothing at all.</summary>
    private const int Or = 1;

    private const int And = 2;

    private const int Comparison = 3;

    private const int Additive = 4;

    private const int Multiplicative = 5;

    private const int Prefix = 6;

    private const int Atom = 7;

    public static string Write(ExpressionNode node, string hole)
        => Piece(node, hole, out _);

    private static string Piece(ExpressionNode node, string hole, out int precedence)
    {
        switch (node)
        {
            case NumberNode number:
                precedence = Atom;
                return number.Text;

            case TextNode text:
                precedence = Atom;
                return Quoted(text.Text);

            case VariableNode variable:
                precedence = Atom;
                return "$" + variable.Name;

            case ConstantNode constant:
                precedence = Atom;
                return constant.Word;

            case ListNode list:
                precedence = Atom;
                return "[" + string.Join(", ", list.Items.Select(item => Piece(item, hole, out _))) + "]";

            case CallNode call:
                precedence = Atom;
                return call.Name + "("
                    + string.Join(", ", call.Arguments.Select(argument => Piece(argument, hole, out _)))
                    + ")";

            case UnaryNode unary:
                precedence = Prefix;
                var operand = Piece(unary.Operand, hole, out var operandPrecedence);
                return (unary.Operator == "not" ? "not " : "-")
                    + Loose(operand, operandPrecedence, Prefix, false, false);

            case BinaryNode binary:
                precedence = Binds(binary.Operator);
                var left = Piece(binary.Left, hole, out var leftPrecedence);
                var right = Piece(binary.Right, hole, out var rightPrecedence);
                return Loose(left, leftPrecedence, precedence, false, false)
                    + " " + binary.Operator + " "
                    + Loose(right, rightPrecedence, precedence, true, !ReadsTheSame(binary.Operator));

            case HoleNode:
                precedence = Atom;
                return hole;

            default:
                throw new InvalidOperationException($"no way to write {node.GetType().Name}");
        }
    }

    /// <summary>How tightly an operator holds its operands, which is the order the language reads them in.</summary>
    private static int Binds(string operation) => operation switch
    {
        "or" => Or,
        "and" => And,
        "==" or "!=" or "<" or "<=" or ">" or ">=" => Comparison,
        "+" or "-" => Additive,
        _ => Multiplicative,
    };

    /// <summary>
    /// True for the operators whose right operand needs no brackets when it binds exactly as
    /// tightly: what they do to their two operands in turn comes out the same either way. The rest
    /// — subtraction, multiplication, division, the remainder and the comparisons — read
    /// differently nested the other way round, so their right operand keeps its brackets.
    /// </summary>
    private static bool ReadsTheSame(string operation) => operation is "+" or "and" or "or";

    private static string Loose(string text, int child, int parent, bool right, bool tight)
        => child < parent || (tight && child == parent && right) ? "(" + text + ")" : text;

    /// <summary>The text with the quotes the language reads it back through.</summary>
    private static string Quoted(string text)
    {
        var builder = new StringBuilder("\"");
        foreach (var character in text)
        {
            builder.Append(character switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ => character.ToString(),
            });
        }

        return builder.Append('"').ToString();
    }
}
