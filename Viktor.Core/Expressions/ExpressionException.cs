using System;

namespace Viktor.Core.Expressions;

/// <summary>Why an expression could not be evaluated. The interface turns these into text.</summary>
public enum ExpressionErrorCode
{
    /// <summary>The text is not a well formed expression.</summary>
    Syntax,

    /// <summary>The expression reads a variable that is not defined.</summary>
    UnknownVariable,

    /// <summary>The expression calls a function that does not exist.</summary>
    UnknownFunction,

    /// <summary>A function was called with the wrong number of arguments.</summary>
    ArgumentCount,

    /// <summary>An argument or operand had the wrong kind of value.</summary>
    TypeMismatch,

    /// <summary>Something was divided by zero.</summary>
    DivideByZero,

    /// <summary>A list index pointed outside the list.</summary>
    IndexOutOfRange,
}

/// <summary>
/// Raised while an expression is read or evaluated. The message stays English and
/// machine readable; the details travel separately so the interface can translate them.
/// </summary>
public sealed class ExpressionException : Exception
{
    public ExpressionException(ExpressionErrorCode code, string detail)
        : base($"{code}: {detail}")
    {
        Code = code;
        Detail = detail;
    }

    /// <summary>Which rule was broken.</summary>
    public ExpressionErrorCode Code { get; }

    /// <summary>The offending name or text, shown to the user next to the translated reason.</summary>
    public string Detail { get; }

    public static ExpressionException Syntax(string detail) => new(ExpressionErrorCode.Syntax, detail);

    public static ExpressionException UnknownVariable(string name) => new(ExpressionErrorCode.UnknownVariable, "$" + name);

    public static ExpressionException UnknownFunction(string name) => new(ExpressionErrorCode.UnknownFunction, name);

    public static ExpressionException ArgumentCount(string detail) => new(ExpressionErrorCode.ArgumentCount, detail);

    public static ExpressionException TypeMismatch(string detail) => new(ExpressionErrorCode.TypeMismatch, detail);

    public static ExpressionException DivideByZero() => new(ExpressionErrorCode.DivideByZero, "0");

    public static ExpressionException IndexOutOfRange(string detail) => new(ExpressionErrorCode.IndexOutOfRange, detail);
}
