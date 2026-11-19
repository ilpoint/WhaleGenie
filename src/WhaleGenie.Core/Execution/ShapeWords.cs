using System;
using System.Linq;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// The word a step's shape holds, read from the shape itself rather than from a line rendered and
/// read back. A name answers with the name, a run with the words it is made of, and anything else
/// with the plain value it was written as. There is no sign in front of a name to strip.
/// </summary>
internal static class ShapeWords
{
    public static string Of(ExpressionNode? value) => value switch
    {
        null => string.Empty,
        VariableNode variable => variable.Name.Trim(),
        ConcatNode run => string.Concat(run.Parts.Select(Of)),
        _ => Formula.Plain(value).Trim(),
    };
}
