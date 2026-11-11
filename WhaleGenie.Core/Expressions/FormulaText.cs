using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Expressions;

/// <summary>
/// Reads the line a macro file holds into the shape a run works with.
///
/// <para>
/// This is the one place in the program where a character in a piece of text says "a variable
/// starts here". It exists because the file still keeps its values as lines of text, and the
/// engine has stopped reading them: everything past this point works with shapes, so the run, the
/// checks and the editor have no language of their own to get wrong.
/// </para>
///
/// <para>
/// It is meant to shrink and then go. Once a field keeps its own shape in the file, nothing calls
/// this; until then it is the single seam, rather than a second way of reading values spread over
/// the runner.
/// </para>
/// </summary>
public static class FormulaText
{
    /// <summary>
    /// The names a field reads, in the order they appear. A name standing in braces as a formula
    /// counts too, because the formula may read one.
    /// </summary>
    public static IReadOnlyList<string> Names(string? text, ActionReadMode mode)
    {
        if (mode == ActionReadMode.VariableName)
        {
            var name = (text ?? string.Empty).Trim().TrimStart('$');
            return VariableName.IsName(name) ? [name] : [];
        }

        if (mode == ActionReadMode.WholeValue
            && Expression.TryParse(text, out var whole, out _) && whole is not null)
        {
            return Expression.References(whole);
        }

        try
        {
            return Expression.References(ReadRun(text ?? string.Empty));
        }
        catch (ExpressionException)
        {
            return [];
        }
    }

    /// <summary>
    /// One name in a field rewritten, with everything else left exactly as it was written. This
    /// keeps the spacing, the quotes and the braces of the line, which is what makes a rename
    /// touch one name rather than tidy up a formula nobody asked to have tidied.
    /// </summary>
    public static string Rename(string? text, ActionReadMode mode, string oldName, string newName)
    {
        var source = text ?? string.Empty;
        if (source.Length == 0 || oldName.Length == 0
            || string.Equals(oldName, newName, StringComparison.Ordinal))
        {
            return source;
        }

        if (mode == ActionReadMode.VariableName)
        {
            var written = source.Trim();
            var sign = written.StartsWith('$');
            var name = sign ? written[1..].Trim() : written;
            return string.Equals(name, oldName, StringComparison.Ordinal)
                ? (sign ? "$" : string.Empty) + newName
                : source;
        }

        var rewritten = new StringBuilder(source.Length);
        var quoted = false;
        var escaped = false;
        for (var index = 0; index < source.Length; index++)
        {
            var character = source[index];
            if (quoted)
            {
                rewritten.Append(character);
                if (escaped)
                {
                    escaped = false;
                }
                else if (character == '\\')
                {
                    escaped = true;
                }
                else if (character == '"')
                {
                    quoted = false;
                }

                continue;
            }

            if (character == '"')
            {
                quoted = true;
                rewritten.Append(character);
                continue;
            }

            if (character == '$' && index + 1 < source.Length && source[index + 1] == '$')
            {
                rewritten.Append("$$");
                index++;
                continue;
            }

            if (character != '$' || !At(source, index + 1, out var name, out var end, out var braced))
            {
                rewritten.Append(character);
                continue;
            }

            if (braced)
            {
                var inside = VariableName.IsName(name)
                    ? string.Equals(name, oldName, StringComparison.Ordinal) ? newName : name
                    : RenameFormula(name, oldName, newName);
                rewritten.Append("${").Append(inside).Append('}');
            }
            else if (string.Equals(name, oldName, StringComparison.Ordinal))
            {
                rewritten.Append('$').Append(newName);
            }
            else
            {
                rewritten.Append('$').Append(name);
            }

            index = end - 1;
        }

        return rewritten.ToString();
    }

    /// <summary>
    /// A braced formula with one name rewritten. The formula's own spacing is kept where it can
    /// be: an expression that reads is rewritten name by name, and one that does not is left
    /// alone rather than half-rewritten.
    /// </summary>
    private static string RenameFormula(string formula, string oldName, string newName)
        => Expression.TryParse(formula, out var tree, out _) && tree is not null
            ? Expression.Render(Expression.Rename(tree, oldName, newName))
            : formula;

    /// <summary>
    /// The value a field's line stands for, read the way the catalogue says that field is read: a
    /// name being written, one value, or a run of text with names in it.
    /// </summary>
    public static ExpressionNode Read(string? text, ActionReadMode mode)
    {
        var written = text ?? string.Empty;
        try
        {
            return mode switch
            {
                ActionReadMode.VariableName => ReadName(written),
                ActionReadMode.WholeValue => ReadWhole(written),
                _ => ReadRun(written),
            };
        }
        catch (ExpressionException fault)
        {
            return new FaultNode(written, fault.Message);
        }
    }

    /// <summary>
    /// The line a shape is written as. This is the other half of <see cref="Read"/>, and it is a
    /// rendering: it goes to a log, a failure message, or a place that still speaks lines, and
    /// nothing reads a value back out of it — the shape is the value.
    ///
    /// <para>
    /// A run is written as its pieces standing next to each other, which is what it was read from;
    /// a formula is written back with the brackets and the spacing that make it read the same way
    /// again. A piece of text keeps every character it holds, so a dollar in it is doubled: that is
    /// how the reader is told it is a dollar rather than the start of a name.
    /// </para>
    /// </summary>
    public static string Write(ExpressionNode? value) => value switch
    {
        null => string.Empty,
        TextNode text => WriteLiteral(text.Text),
        FaultNode fault => fault.Text,
        ConcatNode run => WriteRun(run),
        _ => Expression.Render(value),
    };

    /// <summary>
    /// A piece of text as the reader will take it back: a dollar is doubled where it would
    /// otherwise be read as the start of a name, a brace or another doubled dollar, and left alone
    /// everywhere else, so a price or a shell variable keeps the shape it was written in.
    /// </summary>
    private static string WriteLiteral(string text)
    {
        if (!text.Contains('$', StringComparison.Ordinal))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length + 4);
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character != '$')
            {
                builder.Append(character);
                continue;
            }

            var next = index + 1 < text.Length ? text[index + 1] : '\0';
            if (next == '{' || next == '$' || VariableName.IsStart(next))
            {
                builder.Append("$$");
                continue;
            }

            builder.Append('$');
        }

        return builder.ToString();
    }

    /// <summary>
    /// A run of pieces as the one line it was written as. A name followed by something that would
    /// otherwise be read as part of it goes in braces, which is the only reason those braces exist.
    /// </summary>
    private static string WriteRun(ConcatNode run)
    {
        var written = run.Parts
            .Select(part => part is VariableNode variable ? "$" + variable.Name : Write(part))
            .ToList();

        for (var index = 0; index + 1 < written.Count; index++)
        {
            if (run.Parts[index] is not VariableNode variable)
            {
                continue;
            }

            var next = written[index + 1];
            if (next.Length > 0 && VariableName.IsPart(next[0]))
            {
                written[index] = "${" + variable.Name + "}";
            }
        }

        return string.Concat(written);
    }

    /// <summary>
    /// A field that names a variable. The name is what it says, so it stays text: a field that
    /// declares a name is not reading one, and a reference here would make the macro look as if it
    /// read a variable it is only about to write. Anything that does read it — the runner, when the
    /// step asks what the name holds — looks the name up itself.
    /// </summary>
    private static ExpressionNode ReadName(string text)
    {
        var name = text.Trim();
        return Formula.Text(name.StartsWith('$') ? name[1..].Trim() : name);
    }

    /// <summary>
    /// A field that holds one value. It is read as a formula first — that is what makes
    /// <c>1 + 2</c> and <c>upper(name)</c> work — and as a run of text when it is not one, which
    /// is what keeps a sentence, a path or a selector a sentence.
    /// </summary>
    private static ExpressionNode ReadWhole(string text)
    {
        if (Expression.TryParse(text, out var tree, out var fault) && tree is not null)
        {
            return tree;
        }

        // Two signs are how a literal one is written, which is what a price or a shell variable
        // in a value cell looks like. That has to be settled before the strictness below, or
        // "$100 off" would be read as a formula that has to work and would fail instead.
        if (text.TrimStart().StartsWith("$$", StringComparison.Ordinal))
        {
            return ReadRun(text);
        }

        // A line that opens with the sign was written as a value, so it has to work: a mistyped
        // formula is told about rather than quietly becoming the words it looks like. Anything
        // else is a sentence that happens not to be a formula.
        if (text.TrimStart().StartsWith('$'))
        {
            throw fault!;
        }

        return ReadRun(text);
    }

    /// <summary>
    /// A run of text with names in it. A doubled sign is one sign, a name in braces is a name
    /// written where its end would otherwise be unclear, and braces may also hold a formula.
    /// Everything else is text, and a run that is only text is that text.
    /// </summary>
    private static ExpressionNode ReadRun(string text)
    {
        if (text.Length == 0)
        {
            return Formula.Nothing;
        }

        var parts = new List<ExpressionNode>();
        var literal = new StringBuilder();

        void Flush()
        {
            if (literal.Length > 0)
            {
                parts.Add(Formula.Text(literal.ToString()));
                literal.Clear();
            }
        }

        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '$')
            {
                literal.Append(text[index]);
                continue;
            }

            if (index + 1 < text.Length && text[index + 1] == '$')
            {
                literal.Append('$');
                index++;
                continue;
            }

            if (!At(text, index + 1, out var name, out var end, out var braced))
            {
                literal.Append('$');
                continue;
            }

            // A braced name is a reference; a braced formula is that formula, kept as a shape so
            // it is worked out where it stands rather than pasted in as text.
            var value = braced && !VariableName.IsName(name)
                ? Expression.TryParse(name, out var formula, out var fault) && formula is not null
                    ? formula
                    : throw fault!
                : Formula.Name(name);

            Flush();
            parts.Add(value);
            index = end - 1;
        }

        Flush();
        return Formula.Run(parts);
    }

    /// <summary>
    /// The name at <paramref name="start"/>, plainly or in braces, or false when none starts
    /// there. A brace is the way to say where a name ends when the text goes on with a character
    /// that would otherwise be read as part of it.
    /// </summary>
    private static bool At(string text, int start, out string name, out int end, out bool braced)
    {
        name = string.Empty;
        end = start;
        braced = false;

        if (start < text.Length && text[start] == '{')
        {
            var close = Closing(text, start + 1);
            if (close < 0)
            {
                return false;
            }

            var inside = text[(start + 1)..close];
            if (inside.Length == 0)
            {
                return false;
            }

            name = inside;
            end = close + 1;
            braced = true;
            return true;
        }

        if (start >= text.Length || !VariableName.IsStart(text[start]))
        {
            return false;
        }

        end = start + 1;
        while (end < text.Length && VariableName.IsPart(text[end]))
        {
            end++;
        }

        name = text[start..end];
        return true;
    }

    /// <summary>
    /// The brace closing the one at <paramref name="start"/>, skipping braces inside quoted text
    /// and counting nested ones: a formula may hold a literal brace — a regex, a piece of JSON —
    /// and stopping at the first one would cut it in half.
    /// </summary>
    private static int Closing(string text, int start)
    {
        var depth = 0;
        var quoted = false;
        var escaped = false;
        for (var index = start; index < text.Length; index++)
        {
            var character = text[index];
            if (quoted)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (character == '\\')
                {
                    escaped = true;
                }
                else if (character == '"')
                {
                    quoted = false;
                }

                continue;
            }

            if (character == '"')
            {
                quoted = true;
            }
            else if (character == '{')
            {
                depth++;
            }
            else if (character == '}')
            {
                if (depth == 0)
                {
                    return index;
                }

                depth--;
            }
        }

        return -1;
    }
}
