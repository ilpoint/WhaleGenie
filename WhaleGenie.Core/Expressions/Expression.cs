using System;
using System.Collections.Generic;

namespace WhaleGenie.Core.Expressions;

/// <summary>
/// Reads and evaluates the small expression language the editor offers. The grammar stays
/// narrow on purpose: literals, <c>$variables</c>, the usual operators and a table of
/// functions. That is enough for the values a macro needs without turning into a script host.
/// </summary>
/// <remarks>
/// Examples: <c>$count + 1</c>, <c>upper($name)</c>, <c>split($text, ",")</c>,
/// <c>append($names, "Ada")</c>, <c>if($count &gt; 3, "many", "few")</c>.
/// </remarks>
public static class Expression
{
    /// <summary>Every built-in function, in the order the editor should list them.</summary>
    public static IReadOnlyList<ExpressionFunction> Functions => FunctionLibrary.Metadata;

    /// <summary>
    /// The variable names an expression reads, in the order they appear and without
    /// duplicates. Text that is not yet a valid expression still yields whatever names it
    /// holds, which is what the Variable Center needs while a macro is still being written.
    /// </summary>
    public static IReadOnlyList<string> ReferencedNames(string? source)
    {
        var names = new List<string>();
        if (string.IsNullOrEmpty(source))
        {
            return names;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < source.Length; index++)
        {
            if (source[index] != '$')
            {
                continue;
            }

            var start = index + 1;
            if (start >= source.Length || !(char.IsLetter(source[start]) || source[start] == '_'))
            {
                continue;
            }

            var end = start;
            while (end < source.Length && IsNameCharacter(source[end]))
            {
                end++;
            }

            var name = source[start..end];
            if (seen.Add(name))
            {
                names.Add(name);
            }

            index = end - 1;
        }

        return names;
    }

    /// <summary>
    /// Characters a variable name may hold. A hyphen is deliberately left out so that
    /// <c>$count-1</c> reads as a subtraction rather than as a variable named "count-1".
    /// </summary>
    private static bool IsNameCharacter(char character)
        => char.IsLetterOrDigit(character) || character is '_' or '.';

    /// <summary>Evaluates an expression, throwing when it cannot be read.</summary>
    public static Value Evaluate(string? source, IVariableResolver? variables = null)
    {
        if (!TryEvaluate(source, variables, out var value, out var error))
        {
            throw error!;
        }

        return value;
    }

    /// <summary>
    /// Evaluates an expression and reports failure instead of throwing, which is what the
    /// editor uses to show a live error under the field.
    /// </summary>
    public static bool TryEvaluate(string? source, IVariableResolver? variables,
        out Value value, out ExpressionException? error)
    {
        try
        {
            value = EvaluateChecked(source, variables);
            error = null;
            return true;
        }
        catch (ExpressionException failure)
        {
            value = Value.Null;
            error = failure;
            return false;
        }
    }

    /// <summary>
    /// Reads a formula into the tree the editor shows. The tree is the language's own shape, so
    /// what the editor puts in front of the user and what the engine runs are the same thing.
    /// </summary>
    public static ExpressionNode Parse(string? source)
    {
        if (!TryParse(source, out var tree, out var error))
        {
            throw error!;
        }

        return tree!;
    }

    /// <summary>Reads a formula into a tree, reporting failure instead of throwing.</summary>
    public static bool TryParse(string? source, out ExpressionNode? tree,
        out ExpressionException? error)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            tree = null;
            error = ExpressionException.Syntax("the formula is empty");
            return false;
        }

        try
        {
            tree = ExpressionReader.Read(source);
            error = null;
            return true;
        }
        catch (ExpressionException failure)
        {
            tree = null;
            error = failure;
            return false;
        }
    }

    /// <summary>
    /// Writes a tree back out as a formula. A place nothing was put in is written as
    /// <paramref name="hole"/>, which is what the editor shows and never what the engine reads.
    /// </summary>
    public static string Render(ExpressionNode tree, string hole = "▢")
        => ExpressionWriter.Write(tree, hole);

    private static Value EvaluateChecked(string? source, IVariableResolver? variables)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return Value.Null;
        }

        var tree = ExpressionReader.Read(source);
        return ExpressionCompiler.Compile(tree, variables ?? EmptyVariableResolver.Instance)();
    }
}
