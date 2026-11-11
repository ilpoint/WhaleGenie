using System.Collections.Generic;
using System.Linq;

namespace WhaleGenie.Core.Expressions;

/// <summary>
/// Building and reading the values a field holds. A field keeps a shape rather than a line of
/// text, and these are the few things every caller of one needs: a piece of text as a value, a run
/// of pieces as the one value it reads as, and the text of a value that names nothing.
///
/// Keeping the constructors here is what stops a caller from writing a shape by hand and guessing
/// wrong about which of the four kinds it should be.
/// </summary>
public static class Formula
{
    /// <summary>The value a field that says nothing holds.</summary>
    public static ExpressionNode Nothing { get; } = new TextNode(string.Empty);

    /// <summary>One piece of text as the whole value of a field.</summary>
    public static ExpressionNode Text(string text) => new TextNode(text ?? string.Empty);

    /// <summary>
    /// The value a line typed into a value field stands for. A value field is a formula rather
    /// than prose, so the line is read as one; a line that is not a formula — <c>hello there</c>,
    /// <c>C:\a b</c> — is the text it looks like. This is the one place text becomes a shape, and
    /// it is only ever called where a person typed into a whole-value field.
    /// </summary>
    public static ExpressionNode FromText(string? text)
    {
        var written = text ?? string.Empty;
        if (written.Trim().Length == 0)
        {
            return Nothing;
        }

        return Expression.TryParse(written, out var tree, out _) && tree is not null
            ? tree
            : Text(written);
    }

    /// <summary>One variable read by name. A record's field is part of the name, as it always was.</summary>
    public static ExpressionNode Name(string name) => new VariableNode(name);

    /// <summary>
    /// Several pieces standing next to each other. One piece is that piece: a run of one is a
    /// shape nobody needs, and keeping it flat is what lets a field that holds only text be read
    /// as the text it is.
    /// </summary>
    public static ExpressionNode Run(IEnumerable<ExpressionNode> parts)
    {
        var items = parts.Where(part => part is not TextNode { Text: "" }).ToList();
        return items.Count switch
        {
            0 => Nothing,
            1 => items[0],
            _ => new ConcatNode(items),
        };
    }

    /// <summary>True when the field said nothing at all.</summary>
    public static bool IsNothing(ExpressionNode? value) => value is null or TextNode { Text: "" };

    /// <summary>
    /// The value as it is read by a person: the words as written, a name between corner marks so
    /// it is told apart from the prose around it. This is the form a step list, a run log and a
    /// complaint about a step use — never a form anything is read back from, which is what keeps
    /// the marks out of the language.
    /// </summary>
    public static string Speak(ExpressionNode? value) => value switch
    {
        TextNode text => text.Text,
        NumberNode number => number.Text,
        ConstantNode constant => constant.Word,
        VariableNode variable => "「" + variable.Name + "」",
        ConcatNode run => string.Concat(run.Parts.Select(Speak)),
        ListNode list => "[" + string.Join(", ", list.Items.Select(Speak)) + "]",
        CallNode call => call.Name + "(" + string.Join(", ", call.Arguments.Select(Speak)) + ")",
        UnaryNode unary => unary.Operator == "not" ? "not " + Speak(unary.Operand)
            : "-" + Speak(unary.Operand),
        BinaryNode binary => Speak(binary.Left) + " " + binary.Operator + " " + Speak(binary.Right),
        _ => string.Empty,
    };

    /// <summary>True when the value names nothing: it is text, a number, or pieces of those.</summary>
    public static bool IsPlain(ExpressionNode? value) => value switch
    {
        null => true,
        TextNode or NumberNode or ConstantNode => true,
        ConcatNode run => run.Parts.All(IsPlain),
        _ => false,
    };

    /// <summary>
    /// The text a value that names nothing comes to, or empty when it names anything. A caller
    /// that wants the value itself asks the runner to work it out; this is for the places that
    /// need a word — a result's name, a key, a choice — and must not be handed a formula.
    /// </summary>
    public static string Plain(ExpressionNode? value) => value switch
    {
        TextNode text => text.Text,
        NumberNode number => number.Text,
        ConstantNode constant => constant.Word,
        ConcatNode run => string.Concat(run.Parts.Select(Plain)),
        _ => string.Empty,
    };
}
