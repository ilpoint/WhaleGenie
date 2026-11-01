using WhaleGenie.Core.Execution;

namespace WhaleGenie.ViewModels;

/// <summary>
/// One colour a pattern wants around its base colour, as the form lists it: where it sits relative
/// to that colour, and which colour has to be there. Read-only on purpose — the list is the pattern
/// itself, and the window that picks it is the one place it is changed.
/// </summary>
public sealed class ColourPointRow
{
    public required int Number { get; init; }

    /// <summary>The number as the list shows it.</summary>
    public string Label => Number.ToString(System.Globalization.CultureInfo.CurrentCulture) + ".";

    /// <summary>The offset as it is written, which may be a sum or a variable rather than a number.</summary>
    public required string Offset { get; init; }

    /// <summary>The colour, written as the field holds it; a variable stands as it is.</summary>
    public required string Colour { get; init; }

    /// <summary>One line of a pattern, or null when the line is not three parts.</summary>
    public static ColourPointRow? From(int number, string line)
    {
        var parts = ColourPointText.Split(line);
        return parts.Count == 3
            ? new ColourPointRow { Number = number, Offset = $"({parts[0]}, {parts[1]})", Colour = parts[2] }
            : null;
    }
}
