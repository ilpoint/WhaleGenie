using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Localization;

/// <summary>
/// How a formula reads in Chinese: one line saying what the piece is doing in the words a reader who
/// does not read formulas would use. It is not what the formula comes to — that is the line above it
/// — it is what the formula says, which is the thing worth checking before running a macro. The
/// sentence is made from the same tree the picture and the text are made of, so the three cannot
/// drift apart, and each function's sentence is written once in the text table beside its name.
/// </summary>
public static class ExpressionPhrase
{
    /// <summary>
    /// The formula said out loud. <paramref name="nameOf"/> says what a variable is called to the
    /// reader; without it the name written in the formula stands for itself.
    /// </summary>
    public static string Say(ExpressionNode tree, Func<string, string>? nameOf = null)
        => Word(tree, nameOf ?? (name => name));

    private static string Word(ExpressionNode node, Func<string, string> nameOf) => node switch
    {
        HoleNode => Strings.Get("Expr.Say.Hole"),
        NumberNode number => number.Text,
        TextNode text => text.Text,
        VariableNode variable => nameOf(variable.Name),
        ConstantNode constant => Strings.Get($"Expr.Say.Value.{Constant(constant.Word)}"),
        UnaryNode unary => Said(Sign(unary.Operator), [unary.Operand], null, nameOf),
        BinaryNode binary => Said(Operator(binary.Operator), [binary.Left, binary.Right], null, nameOf),
        ListNode list => Said(Strings.Get("Expr.Say.List"), list.Items, null, nameOf),
        CallNode call => Call(call, nameOf),
        _ => string.Empty,
    };

    /// <summary>What a call says: the sentence the table writes for that function, with its arguments in.</summary>
    private static string Call(CallNode call, Func<string, string> nameOf)
    {
        var function = Expression.Functions.FirstOrDefault(
            one => string.Equals(one.Name, call.Name, StringComparison.OrdinalIgnoreCase));

        // The parser only lets a known function through, so a name with no sentence beside it is a
        // tree built by something else; that one is read back as the call it is rather than dropped.
        return function is null
            ? call.Name + "(" + string.Join(", ", call.Arguments.Select(argument => Word(argument, nameOf)))
                + ")"
            : Said(Strings.Get($"Expr.Say.{function.Name}"), call.Arguments, function, nameOf);
    }

    /// <summary>A sentence with its arguments filled in, the way the table writes that function.</summary>
    private static string Said(string pattern, IReadOnlyList<ExpressionNode> arguments,
        ExpressionFunction? function, Func<string, string> nameOf)
        => Filled(pattern, Numbered(pattern) + 1, arguments, function, nameOf);

    /// <summary>
    /// A pattern with its arguments filled in. <c>{0}</c>, <c>{1}</c> and so on are what those
    /// arguments say, and <c>{...}</c> is every argument from <paramref name="rest"/> on, joined with
    /// 「、」. A piece in square brackets is said only when every argument it names was written, which
    /// is how one sentence per function still reads well once an argument that may be left out has
    /// been left out.
    /// </summary>
    private static string Filled(string pattern, int rest, IReadOnlyList<ExpressionNode> arguments,
        ExpressionFunction? function, Func<string, string> nameOf)
    {
        var open = pattern.IndexOf('[', StringComparison.Ordinal);
        var close = open < 0 ? -1 : pattern.IndexOf(']', open);
        if (close > open)
        {
            var piece = pattern[(open + 1)..close];
            var kept = Complete(piece, arguments, rest) ? Filled(piece, rest, arguments, function, nameOf)
                : string.Empty;
            return Filled(pattern[..open] + kept + pattern[(close + 1)..], rest, arguments, function, nameOf);
        }

        var said = new StringBuilder();
        var at = 0;
        while (at < pattern.Length)
        {
            var start = pattern.IndexOf('{', at);
            var end = start < 0 ? -1 : pattern.IndexOf('}', start);
            if (start < 0 || end < 0)
            {
                said.Append(pattern, at, pattern.Length - at);
                break;
            }

            var place = pattern[(start + 1)..end];
            var (phrase, plain) = place is "..."
                ? Rest(arguments, rest, function, nameOf)
                : Argument(place, arguments, function, nameOf);

            // What goes around an argument is the pattern's business, and it says which way round it
            // wants it: a place written between 「【」 and 「】」 keeps its brackets around a plain value
            // and drops them around a phrase that already stands on its own, so a formula built out of
            // formulas — 取【剪贴板】里的【第1项】的【月份】字段 — reads as one sentence instead of as
            // brackets inside brackets. A place written between 「（」 and 「）」 wants the opposite,
            // which is what keeps 1加上2乘上3 from being read the wrong way round: the brackets go
            // where the operand is a phrase of its own, and are left off a plain value.
            var before = start > at ? pattern[start - 1] : '\0';
            var after = end + 1 < pattern.Length ? pattern[end + 1] : '\0';
            var valued = before == '【' && after == '】';
            var grouped = before == '（' && after == '）';
            said.Append(pattern, at, (valued || grouped ? start - 1 : start) - at);
            said.Append(valued ? plain ? "【" + phrase + "】" : phrase
                : grouped ? plain ? phrase : "（" + phrase + "）"
                : phrase);
            at = end + (valued || grouped ? 2 : 1);
        }

        return said.ToString();
    }

    /// <summary>One argument of a pattern, and whether it is a value rather than a sentence of its own.</summary>
    private static (string Phrase, bool Plain) Argument(string place,
        IReadOnlyList<ExpressionNode> arguments, ExpressionFunction? function, Func<string, string> nameOf)
    {
        if (!int.TryParse(place, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            || index >= arguments.Count)
        {
            return (string.Empty, true);
        }

        var argument = arguments[index];
        return (Counted(argument, Named(function, index), Word(argument, nameOf)), Plain(argument));
    }

    /// <summary>The arguments after the one a pattern names last, as one list of things.</summary>
    private static (string Phrase, bool Plain) Rest(IReadOnlyList<ExpressionNode> arguments, int from,
        ExpressionFunction? function, Func<string, string> nameOf)
    {
        var spoken = arguments.Skip(from)
            .Select((argument, at) => Counted(argument, Named(function, from + at), Word(argument, nameOf)));
        return (string.Join("、", spoken), arguments.Skip(from).All(Plain));
    }

    /// <summary>
    /// What one argument says, with a place counted the way a person counts places: 序号 and 起始位置
    /// are written from zero in a formula and said from one here, because 第0项 is not something
    /// anyone says. Only a number written down is counted that way — a variable or a formula is read
    /// out as it stands, which is what the formula itself says.
    /// </summary>
    private static string Counted(ExpressionNode argument, string name, string phrase)
        => argument is NumberNode number && name is "start" or "index"
            && double.TryParse(number.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var at)
            && Math.Round(at) == at
                ? ((long)at + 1).ToString(CultureInfo.InvariantCulture)
                : phrase;

    /// <summary>
    /// True when what an argument says is a value rather than a sentence: a number, a piece of text,
    /// a variable, one of the words for true and false, or a place still to fill.
    /// </summary>
    private static bool Plain(ExpressionNode argument)
        => argument is NumberNode or TextNode or VariableNode or ConstantNode or HoleNode;

    /// <summary>True when everything a piece names was written, so the piece has something to say.</summary>
    private static bool Complete(string piece, IReadOnlyList<ExpressionNode> arguments, int rest)
    {
        for (var at = piece.IndexOf('{', StringComparison.Ordinal); at >= 0;
             at = piece.IndexOf('{', at + 1))
        {
            var end = piece.IndexOf('}', at);
            if (end < 0)
            {
                break;
            }

            var place = piece[(at + 1)..end];
            if (place is "...")
            {
                if (arguments.Count <= rest)
                {
                    return false;
                }
            }
            else if (!int.TryParse(place, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                || index >= arguments.Count)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The highest argument number a pattern names, or -1 when it names none.</summary>
    private static int Numbered(string pattern)
    {
        var highest = -1;
        for (var at = pattern.IndexOf('{', StringComparison.Ordinal); at >= 0;
             at = pattern.IndexOf('{', at + 1))
        {
            var end = pattern.IndexOf('}', at);
            if (end > at
                && int.TryParse(pattern[(at + 1)..end], NumberStyles.None,
                    CultureInfo.InvariantCulture, out var index))
            {
                highest = Math.Max(highest, index);
            }
        }

        return highest;
    }

    /// <summary>
    /// What the argument at a place is called in the signature. A function that takes more than its
    /// signature names repeats the last name, the same way its lines on the structure page do.
    /// </summary>
    private static string Named(ExpressionFunction? function, int index)
        => function is null || function.Parameters.Count == 0
            ? string.Empty
            : function.Parameters[Math.Min(index, function.Parameters.Count - 1)].Name;

    /// <summary>True and false the way the words that stand for them are written.</summary>
    private static string Constant(string word) => word switch
    {
        "true" => "True",
        "false" => "False",
        _ => "Null",
    };

    /// <summary>
    /// What the sign in front of a piece says, which is why it is not read the way a binary operator
    /// of the same shape is: the minus in -5 is not the minus between two numbers.
    /// </summary>
    private static string Sign(string operation)
        => Strings.Get(operation is "not" ? "Expr.Say.Op.Not" : "Expr.Say.Op.Negate");

    /// <summary>What an operator written between two things says, which is not how it is written.</summary>
    private static string Operator(string operation) => Strings.Get(operation switch
    {
        "+" => "Expr.Say.Op.Add",
        "-" => "Expr.Say.Op.Subtract",
        "*" => "Expr.Say.Op.Multiply",
        "/" => "Expr.Say.Op.Divide",
        "%" => "Expr.Say.Op.Remainder",
        "==" => "Expr.Say.Op.Equals",
        "!=" => "Expr.Say.Op.NotEquals",
        ">" => "Expr.Say.Op.Greater",
        ">=" => "Expr.Say.Op.GreaterOrEqual",
        "<" => "Expr.Say.Op.Less",
        "<=" => "Expr.Say.Op.LessOrEqual",
        "or" => "Expr.Say.Op.Or",
        _ => "Expr.Say.Op.And",
    });
}
