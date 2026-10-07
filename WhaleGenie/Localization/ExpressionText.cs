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
}
