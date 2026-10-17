using System;
using System.Collections.Generic;
using System.Linq;

namespace WhaleGenie.Core.Expressions;

/// <summary>
/// Reads <c>$name</c> in text that is not an expression. A field read as text is filled in where
/// a name stands, and <c>$$</c> is one literal dollar sign; a name the macro does not know is left
/// as it was written so a typo stays visible instead of disappearing.
/// </summary>
public static class VariableText
{
    /// <summary>
    /// The variable names the text reads, in the order they appear and without duplicates. A
    /// doubled dollar sign is an escape rather than the start of a name.
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

            if (!TryName(text, index + 1, out var name, out var end, out _))
            {
                continue;
            }

            if (seen.Add(name))
            {
                names.Add(name);
            }

            index = end - 1;
        }

        return names;
    }

    /// <summary>
    /// Fills every known name in, leaving the escape as one dollar sign. <paramref name="lookup"/>
    /// answers null for a name it does not know, which is written back unchanged.
    /// </summary>
    public static string Replace(string? text, Func<string, string?> lookup)
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

            written.Append(lookup(name) ?? (braced ? "${" + name + "}" : "$" + name));
            index = end - 1;
        }

        return written.ToString();
    }

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
            if (inside.Length == 0
                || !(char.IsLetter(inside[0]) || inside[0] == '_')
                || !inside.All(character => char.IsLetterOrDigit(character)
                    || character is '_' or '.'))
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
