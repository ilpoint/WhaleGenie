using System;
using System.Collections.Generic;
using System.Linq;

namespace WhaleGenie.Core.Expressions;

/// <summary>
/// Reads <c>$name</c> and <c>${…}</c> in text that is not an expression. A field read as text is
/// filled in where a name stands, and <c>$$</c> is one literal dollar sign; a name the macro does
/// not know is left as it was written so a typo stays visible instead of disappearing. Braces may
/// also hold an expression, which is worked out and written as text.
/// </summary>
public static class VariableText
{
    /// <summary>
    /// The variable names the text reads, in the order they appear and without duplicates. A
    /// doubled dollar sign is an escape rather than the start of a name; the names inside a braced
    /// expression count too.
    /// </summary>
    public static IReadOnlyList<string> Referenced(string? text)
    {
        var names = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            return names;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '$')
            {
                continue;
            }

            if (index + 1 < text.Length && text[index + 1] == '$')
            {
                index++;
                continue;
            }

            if (!TryName(text, index + 1, out var name, out var end, out var braced))
            {
                continue;
            }

            if (braced && !IsName(name))
            {
                foreach (var referenced in Expression.ReferencedNames(name))
                {
                    if (seen.Add(referenced))
                    {
                        names.Add(referenced);
                    }
                }
            }
            else if (seen.Add(name))
            {
                names.Add(name);
            }

            index = end - 1;
        }

        return names;
    }

    /// <summary>
    /// The braced parts of the text that are expressions rather than names, in the order they
    /// appear. The editor parses these while the macro is being written, so a broken one is marked
    /// there rather than at the run.
    /// </summary>
    public static IReadOnlyList<string> Expressions(string? text)
    {
        var expressions = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            return expressions;
        }

        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '$')
            {
                continue;
            }

            if (index + 1 < text.Length && text[index + 1] == '$')
            {
                index++;
                continue;
            }

            if (!TryName(text, index + 1, out var name, out var end, out var braced))
            {
                continue;
            }

            if (braced && !IsName(name))
            {
                expressions.Add(name);
            }

            index = end - 1;
        }

        return expressions;
    }

    /// <summary>
    /// Fills every known name in, leaving the escape as one dollar sign. <paramref name="lookup"/>
    /// answers null for a name it does not know, which is written back unchanged. A braced
    /// expression is handed to <paramref name="evaluate"/>; when there is none, or it answers null,
    /// the expression is written back unchanged as well.
    /// </summary>
    public static string Replace(string? text, Func<string, string?> lookup,
        Func<string, string?>? evaluate = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var written = new System.Text.StringBuilder(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '$')
            {
                written.Append(text[index]);
                continue;
            }

            if (index + 1 < text.Length && text[index + 1] == '$')
            {
                written.Append('$');
                index++;
                continue;
            }

            if (!TryName(text, index + 1, out var name, out var end, out var braced))
            {
                written.Append('$');
                continue;
            }

            var value = braced
                ? IsName(name) ? lookup(name) : evaluate?.Invoke(name)
                : lookup(name);
            written.Append(value ?? (braced ? "${" + name + "}" : "$" + name));
            index = end - 1;
        }

        return written.ToString();
    }

    /// <summary>True when the text is a variable name rather than an expression.</summary>
    private static bool IsName(string text)
        => text.Length > 0
           && (char.IsLetter(text[0]) || text[0] == '_')
           && text.All(character => char.IsLetterOrDigit(character) || character is '_' or '.');

    /// <summary>
    /// The name at <paramref name="start"/>, or false when no name starts there. A name may be
    /// written plainly or in braces: <c>${name}</c> is the way to say where a name ends when the
    /// text goes on with a dot or another letter that would otherwise be taken as part of it.
    /// </summary>
    private static bool TryName(string text, int start, out string name, out int end, out bool braced)
    {
        name = string.Empty;
        end = start;
        braced = false;

        if (start < text.Length && text[start] == '{')
        {
            var close = text.IndexOf('}', start + 1);
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

        if (start >= text.Length
            || !(char.IsLetter(text[start]) || text[start] == '_'))
        {
            return false;
        }

        end = start + 1;
        while (end < text.Length
               && (char.IsLetterOrDigit(text[end]) || text[end] is '_' or '.'))
        {
            end++;
        }

        name = text[start..end];
        return true;
    }
}
