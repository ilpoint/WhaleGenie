using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace WhaleGenie.Models;

/// <summary>The one definition of the characters a typed-string entry can hold and end with.</summary>
public static class HotstringCharacters
{
    public const int MaximumLength = 40;

    /// <summary>The default ending characters, stored exactly as the characters the hook receives.</summary>
    public const string DefaultEndChars = "-()[]{}:;'\"/\\,.?! \n\t";

    public static IReadOnlyList<(string Name, string Value)> EndKeyChoices { get; } =
    [
        ("Space", " "), ("Enter", "\n"), ("Tab", "\t"),
        ("-", "-"), ("(", "("), (")", ")"), ("[", "["), ("]", "]"),
        ("{", "{"), ("}", "}"), (":", ":"), (";", ";"), ("'", "'"),
        ("\"", "\""), ("/", "/"), ("\\", "\\"), (",", ","),
        (".", "."), ("?", "?"), ("!", "!"),
    ];

    public static bool IsAllowed(char character)
        => character is >= ' ' and <= '~' or '\t' or '\n';

    public static bool IsAllowed(string text)
        => text.All(IsAllowed);

    public static IReadOnlySet<string> ParseEndChars(string? written)
        => (written ?? DefaultEndChars)
            .Select(character => character.ToString())
            .ToHashSet(StringComparer.Ordinal);

    public static bool IsEndCharacter(string typed, string? configured)
        => ParseEndChars(configured).Contains(typed);

    public static string DescribeEndChars(string? configured)
    {
        var set = new HashSet<string>(ParseEndChars(configured), StringComparer.Ordinal);
        var parts = new List<string>();
        foreach (var (name, value) in EndKeyChoices)
        {
            if (set.Remove(value))
            {
                parts.Add(name);
            }
        }

        foreach (var value in set.Order(StringComparer.Ordinal))
        {
            parts.Add(Display(value));
        }

        return string.Join("、", parts);
    }

    /// <summary>Writes a checked set back as the literal characters typed at the keyboard.</summary>
    public static string WriteEndChars(IEnumerable<string> values)
    {
        var remaining = new HashSet<string>(values, StringComparer.Ordinal);
        var written = new StringBuilder();
        foreach (var (_, value) in EndKeyChoices)
        {
            if (!remaining.Remove(value))
            {
                continue;
            }

            written.Append(value);
        }

        foreach (var value in remaining.Order(StringComparer.Ordinal))
        {
            written.Append(value);
        }

        return written.ToString();
    }

    /// <summary>Makes spaces and control characters visible without changing the stored value.</summary>
    public static string Display(string text)
    {
        var shown = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            shown.Append(character switch
            {
                ' ' => '␠',
                '\t' => '⇥',
                '\n' => '⏎',
                _ => character,
            });
        }

        return shown.ToString();
    }
}
