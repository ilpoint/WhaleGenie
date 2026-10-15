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
    public static string ParameterLabel(ExpressionParameter parameter)
        => Strings.Get($"Expr.Arg.{parameter.Name}.label", parameter.Label);

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
