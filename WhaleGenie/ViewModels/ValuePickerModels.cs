using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;

namespace WhaleGenie.ViewModels;

/// <summary>
/// The signs a formula is built from. Both windows that offer a palette read this one table, so
/// what a sign says and what it writes cannot drift apart between them.
/// </summary>
internal static class OperatorPalette
{
    /// <summary>
    /// Arithmetic first, then comparison, then the words that join conditions. The text inserted is
    /// spaced out so two clicks make a formula that still reads after it is written.
    /// </summary>
    public static IReadOnlyList<ExpressionOperator> Build() =>
    [
        new("+", " + ", "Expr.Op.Add"),
        new("−", " - ", "Expr.Op.Subtract"),
        new("×", " * ", "Expr.Op.Multiply"),
        new("÷", " / ", "Expr.Op.Divide"),
        new("%", " % ", "Expr.Op.Remainder"),
        new("=", " == ", "Expr.Op.Equals"),
        new("≠", " != ", "Expr.Op.NotEquals"),
        new(">", " > ", "Expr.Op.Greater"),
        new("≥", " >= ", "Expr.Op.GreaterOrEqual"),
        new("<", " < ", "Expr.Op.Less"),
        new("≤", " <= ", "Expr.Op.LessOrEqual"),
        new("and", " and ", "Expr.Op.And"),
        new("or", " or ", "Expr.Op.Or"),
        new("not", "not ", "Expr.Op.Not"),
        new("( )", "()", "Expr.Op.Group"),
        new("[ ]", "[]", "Expr.Op.List"),
        new("\"{ }\"", "\"\"", "Expr.Op.Text"),
    ];
}

/// <summary>
/// One button of the dock above the function wall: the group it shows, and what the button says.
/// An empty group is all of them, which is what the dock stands on when the dialog opens.
/// </summary>
public sealed partial class ExpressionFunctionFilter : ObservableObject
{
    public ExpressionFunctionFilter(string group, string labelKey)
    {
        Group = group;
        Label = Strings.Get(labelKey);
    }

    /// <summary>The key every function of this group shares, such as <c>text</c>, or empty for all.</summary>
    public string Group { get; }

    /// <summary>What the button says, in the reader's own language.</summary>
    public string Label { get; }

    /// <summary>
    /// True on the one button that stands for the list on show right now. Without it a row of words
    /// that all look alike leaves the reader guessing which of them the functions below belong to.
    /// </summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>One argument of a function, named and explained the way the bubble shows it.</summary>
public sealed record FunctionArgument(string Label, string Hint);

/// <summary>
/// One function card: what it is called and the text one click puts in the formula. The card says
/// the name in the reader's own language with the name the code calls it by beside it — a reader
/// who has met either one recognises the other — and is painted in the colour of the group it
/// belongs to, which is what tells the groups apart now that no heading does. How the call is
/// written and what each argument is for are in the bubble that comes up when the pointer rests on
/// the card: written on the card itself they would be a paragraph per function on a wall of them.
/// </summary>
public sealed class ExpressionFunctionChoice
{
    public ExpressionFunctionChoice(ExpressionFunction function)
    {
        Function = function;
        Name = function.Name;
        Signature = function.Signature;
        Insert = function.Name + "(";
        Heading = ExpressionText.Heading(function);
        Tag = ExpressionText.InChinese
            ? ExpressionText.ChineseName(function) + " (" + Name + ")"
            : Name;
        ChineseSignature = ExpressionText.ChineseSignature(function);
        // How the call is written, in whichever language the reader is reading: the Chinese form is
        // a reading of the call rather than a copy of it, so an English interface keeps the one the
        // code itself is written with.
        WrittenAs = ExpressionText.InChinese ? ChineseSignature : Signature;
        Arguments =
        [
            .. function.Parameters.Select(parameter => new FunctionArgument(
                ExpressionText.ParameterLabel(function.Name, parameter),
                ExpressionText.ParameterSaid(function.Name, parameter))),
        ];
        // The Chinese line lives in the text table and the English one beside the function in the
        // engine, the way the built-in variables are written: one sentence per language, no copies.
        Hint = Strings.Get($"Expr.Fn.{function.Name}", function.Description);
        ArityNote = NoteArity(function);
    }

    /// <summary>The function itself, which is what the structure page builds its lines from.</summary>
    public ExpressionFunction Function { get; }

    /// <summary>The name the function is called by.</summary>
    public string Name { get; }

    /// <summary>Which group of the table it belongs to, which is the colour its card is painted in.</summary>
    public string Group => Function.Group;

    /// <summary>How it is written, with the arguments it takes.</summary>
    public string Signature { get; }

    /// <summary>What the button says: the name in the reader's own language, the code name beside it.</summary>
    public string Heading { get; }

    /// <summary>One pill's worth of the same: short enough to stand beside its neighbours.</summary>
    public string Tag { get; }

    /// <summary>The same call, with every argument named in the reader's own language.</summary>
    public string ChineseSignature { get; }

    /// <summary>How the call is written, said in the reader's own language.</summary>
    public string WrittenAs { get; }

    /// <summary>
    /// How many places this function asks for, on the card itself: the one thing a card cannot say by
    /// its name alone, and the reason a six-argument function looked like six numbers to fill in.
    /// </summary>
    public string ArityNote { get; }

    /// <summary>The sentence that says how many places a function takes, optional ones included.</summary>
    private static string NoteArity(ExpressionFunction function) => function switch
    {
        { MaxArguments: 0 } => Strings.Get("ExprBuilder.ArityNone"),
        { MaxArguments: int.MaxValue } => Strings.Format("ExprBuilder.ArityAtLeast",
            function.MinArguments),
        { MaxArguments: var most } when most == function.MinArguments =>
            Strings.Format("ExprBuilder.ArityFixed", most),
        { MinArguments: var few, MaxArguments: var most } =>
            Strings.Format("ExprBuilder.ArityRange", few, most - few),
    };

    /// <summary>
    /// What each argument is for, one line each, in the order the call takes them: this is what the
    /// bubble under the pointer is for, and what a card alone cannot say.
    /// </summary>
    public IReadOnlyList<FunctionArgument> Arguments { get; }

    /// <summary>What one click leaves in the expression, with the caret after the bracket.</summary>
    public string Insert { get; }

    /// <summary>One line saying what the function does, shown when the pointer rests on it.</summary>
    public string Hint { get; }
}

/// <summary>One operator button: what it looks like, what it inserts, and what it means.</summary>
public sealed class ExpressionOperator
{
    public ExpressionOperator(string token, string insert, string hintKey)
    {
        Token = token;
        Insert = insert;
        Hint = Strings.Get(hintKey);
    }

    /// <summary>The short sign shown on the button, such as <c>+</c> or <c>and</c>.</summary>
    public string Token { get; }

    /// <summary>The text dropped into the expression, spaced so it reads after insertion.</summary>
    public string Insert { get; }

    /// <summary>
    /// The hue the sign is drawn in, borrowed from the function palette: numbers, comparisons,
    /// words that join, and brackets are four kinds of thing and should not look alike.
    /// </summary>
    public string Tone => Token switch
    {
        "+" or "−" or "×" or "÷" or "%" => "maths",
        "=" or "≠" or ">" or "≥" or "<" or "≤" => "dates",
        "and" or "or" or "not" => "records",
        "\"{ }\"" => "text",
        _ => "other",
    };

    /// <summary>One plain line saying what the operator does.</summary>
    public string Hint { get; }
}
