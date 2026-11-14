using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Variables;

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

    /// <summary>One variable read by name. A record's field is part of the name, as it always was.</summary>
    public static ExpressionNode Name(string name, string id = "") => new VariableNode(name, id);

    /// <summary>
    /// The name a shape names, with the identity it is bound to. A shape that is text names the
    /// text; it has no identity of its own. This is the reader for the fields a step writes a
    /// variable into, where a rendered line would put the name in corner marks.
    /// </summary>
    public static VariableKey NameOf(ExpressionNode? value) => VariableKey.From(value);

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

    /// <summary>
    /// A number as its own shape. A caller that already knows the value need not also spell it, so
    /// this is the short way; the overload below keeps the spelling when a field must hand it back
    /// unchanged (a number typed as <c>1.0</c> stays that way in a step list).
    /// </summary>
    public static ExpressionNode Number(double value)
        => new NumberNode(value.ToString(CultureInfo.InvariantCulture), value);

    public static ExpressionNode Number(string text, double value) => new NumberNode(text, value);

    public static ExpressionNode Date(string text) => new DateNode(text);

    public static ExpressionNode Constant(string word) => new ConstantNode(word);

    public static ExpressionNode Call(string name, params ExpressionNode[] arguments)
        => new CallNode(name, arguments);

    public static ExpressionNode Unary(string op, ExpressionNode operand)
        => new UnaryNode(op, operand);

    public static ExpressionNode Binary(string op, ExpressionNode left, ExpressionNode right)
        => new BinaryNode(op, left, right);

    public static ExpressionNode List(params ExpressionNode[] items) => new ListNode(items);

    /// <summary>The one place in a tree nothing has been put in yet.</summary>
    public static ExpressionNode Hole => HoleNode.Instance;

    /// <summary>True when the field said nothing at all.</summary>
    public static bool IsNothing(ExpressionNode? value) => value is null or TextNode { Text: "" };

    /// <summary>
    /// The word a shape names, for the places that look a field up by what it says — a key, a
    /// kind, the name a result is kept under. A name answers with the name itself and nothing
    /// around it; every other shape is said as <see cref="Speak"/> says it. Step lists and log
    /// lines use <see cref="Speak"/>, where a name is told apart by its corner marks.
    /// </summary>
    public static string Word(ExpressionNode? value) => value switch
    {
        null => string.Empty,
        VariableNode variable => variable.Name,
        ConcatNode run => string.Concat(run.Parts.Select(Word)),
        _ => Speak(value),
    };

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
        DateNode date => date.Text,
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
        TextNode or NumberNode or DateNode or ConstantNode => true,
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
        DateNode date => date.Text,
        ConstantNode constant => constant.Word,
        ConcatNode run => string.Concat(run.Parts.Select(Plain)),
        _ => string.Empty,
    };
}
