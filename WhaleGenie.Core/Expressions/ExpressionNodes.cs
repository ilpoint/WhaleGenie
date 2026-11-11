using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;


namespace WhaleGenie.Core.Expressions;

// The tree an expression is read into.
/// <summary>
/// One piece of an expression: the shape the editor shows and fills in, and the shape the
/// evaluator runs. Reading the text into a tree before thinking about values is what lets the
/// editor show a formula as boxes, so the reader itself stays free of anything the interface
/// needs.
/// </summary>
public abstract record ExpressionNode;

/// <summary>A number, kept as it was written so the text can be handed back unchanged.</summary>
public sealed record NumberNode(string Text, double Value) : ExpressionNode;

/// <summary>A piece of text, held unescaped; the writer adds the quotes and the backslashes.</summary>
public sealed record TextNode(string Text) : ExpressionNode;

/// <summary>
/// Pieces standing next to each other and read as one run: what a field holds when it is prose
/// with a name somewhere in it. The language never writes one — a run is read from the text as a
/// whole, and a formula is one value rather than a run of them — so only the editor makes these,
/// from what the person typed and what they picked out of the variable window.
///
/// This is what makes a name inside a sentence a shape rather than a spelling: there is no
/// character in the run that says "a variable starts here", so no character can be typed by
/// accident, and no text that merely looks like a name can be mistaken for one.
/// </summary>
public sealed record ConcatNode(IReadOnlyList<ExpressionNode> Parts) : ExpressionNode;

/// <summary>A name the macro holds.</summary>
public sealed record VariableNode(string Name) : ExpressionNode;

/// <summary>One of the words that stand for a value on its own: true, false or null.</summary>
public sealed record ConstantNode(string Word) : ExpressionNode;

/// <summary>One operand and the operator in front of it, written <c>not</c> or <c>-</c>.</summary>
public sealed record UnaryNode(string Operator, ExpressionNode Operand) : ExpressionNode;

/// <summary>Two operands and the operator between them.</summary>
public sealed record BinaryNode(string Operator, ExpressionNode Left, ExpressionNode Right)
    : ExpressionNode;

/// <summary>A function and its arguments, in the order they are written.</summary>
public sealed record CallNode(string Name, IReadOnlyList<ExpressionNode> Arguments) : ExpressionNode;

/// <summary>A list written out, such as <c>[1, 2, 3]</c>.</summary>
public sealed record ListNode(IReadOnlyList<ExpressionNode> Items) : ExpressionNode;

/// <summary>
/// A place in the tree nothing has been put in yet. Reading a formula never produces one — text
/// with a space in it does not read at all — so this is the editor's own shape: it stands for a box
/// the user has not filled in, and the writer leaves it as a placeholder.
/// </summary>
public sealed record HoleNode : ExpressionNode
{
    /// <summary>The one empty place, since there is nothing to tell two of them apart.</summary>
    public static HoleNode Instance { get; } = new();
}
