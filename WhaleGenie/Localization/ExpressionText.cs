using System;
using System.Linq;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Localization;

/// <summary>Turns an expression failure into the line a user reads.</summary>
public static class ExpressionText
{
    /// <summary>
    /// The reason, translated, with the offending name or text appended. The engine keeps
    /// its own messages machine readable so the wording can live with the rest of the text.
    /// </summary>
    public static string Describe(ExpressionException error)
        => Strings.Format($"Expr.Error.{error.Code}", error.Detail);

    /// <summary>
    /// What an argument is called in the reader's own language. The name the signature spells out
    /// stays as it is — it is the piece of code being written — and this is what to show beside the
    /// place it goes in.
    /// </summary>
    public static string ParameterLabel(string function, ExpressionParameter parameter)
        => Wording(function, parameter, Strings.Get);

    /// <summary>
    /// What an argument is called: the function's own word for it where it has one — a <c>text</c>
    /// that has to be a date says 日期文字 rather than 原文本 — and the word its name usually goes
    /// by otherwise, which is written once for every function that uses that name.
    /// </summary>
    private static string Wording(string function, ExpressionParameter parameter,
        Func<string, string, string> text)
        => function.Length == 0
            ? text($"Expr.Arg.{parameter.Name}.label", parameter.Label)
            : text($"Expr.Arg.{function}.{parameter.Name}.label",
                text($"Expr.Arg.{parameter.Name}.label", parameter.Label));

    /// <summary>
    /// True while the interface is Chinese, which is where the names written below belong: an
    /// English reader has the code names, and a Chinese name beside them would be a word they have
    /// no use for.
    /// </summary>
    public static bool InChinese => Strings.Current.Language == Language.Chinese;

    /// <summary>
    /// What a function is called in Chinese, which is the name a reader who does not have the code
    /// name by heart reaches for. Chinese is asked for by name rather than through the language the
    /// interface is in: the manual carries these names as well, and it is Chinese whatever the
    /// interface is set to.
    /// </summary>
    public static string ChineseName(ExpressionFunction function)
        => Chinese($"Expr.FnName.{function.Name}", function.Name);

    /// <summary>
    /// What a function is called on a button or on its line: the Chinese name with the name the code
    /// calls it by beside it, so a reader who has met either one of them recognises the other. An
    /// English interface shows the code name and nothing else.
    /// </summary>
    public static string Heading(ExpressionFunction function)
    {
        var named = ChineseName(function);
        return InChinese && named != function.Name
            ? named + " (" + function.Name + ")"
            : function.Name;
    }

    /// <summary>
    /// How a function is written with every argument named in Chinese: the English signature with
    /// the function name and each argument name replaced, and the brackets and dots left exactly
    /// where they are. The shape is therefore written once — it is the English signature that says
    /// how many arguments there are and which of them may be left out.
    /// </summary>
    public static string ChineseSignature(ExpressionFunction function)
    {
        var open = function.Signature.IndexOf('(');
        var close = function.Signature.LastIndexOf(')');
        if (open < 0 || close <= open)
        {
            return ChineseName(function);
        }

        var parts = function.Signature[(open + 1)..close].Split(',').Select(part =>
        {
            // The brackets say an argument may be left out and the dots say more of the same may
            // follow; neither is part of the name, so both stay where the signature puts them.
            var name = part.Replace("[", string.Empty, StringComparison.Ordinal)
                .Replace("]", string.Empty, StringComparison.Ordinal)
                .Replace(".", string.Empty, StringComparison.Ordinal)
                .Trim();
            var parameter = function.Parameters.FirstOrDefault(
                one => string.Equals(one.Name, name, StringComparison.OrdinalIgnoreCase));
            return parameter is null
                ? part
                : part.Replace(name, ChineseLabel(function.Name, parameter), StringComparison.Ordinal);
        });

        return ChineseName(function) + "(" + string.Join(",", parts) + ")";
    }

    /// <summary>The Chinese text for a key, whatever language the interface is in.</summary>
    private static string Chinese(string key, string fallback)
        => Strings.Chinese.TryGetValue(key, out var text) ? text : fallback;

    /// <summary>What an argument is called in Chinese.</summary>
    private static string ChineseLabel(string function, ExpressionParameter parameter)
        => Wording(function, parameter, Chinese);

    /// <summary>
    /// The name the signature spells out and one line saying what goes in it, which is what a
    /// reader needs to match a line to the argument it fills. An argument that means something
    /// narrower than its name usually does carries its own sentence beside the function, and the
    /// Chinese line for it is looked for under the function before the shared one is used.
    /// </summary>
    public static string ParameterHint(string function, ExpressionParameter parameter)
    {
        var said = Strings.Get($"Expr.Arg.{function}.{parameter.Name}.hint",
            Strings.Get($"Expr.Arg.{parameter.Name}.hint", parameter.Hint));
        return said.Length == 0 ? parameter.Name : parameter.Name + " · " + said;
    }
}
