using System;
using System.Collections.Generic;
using System.Linq;

namespace WhaleGenie.Core.Variables;

/// <summary>
/// The identity a variable declaration is given. It is written beside the name so renaming the
/// declaration does not have to find every reference by spelling. Six characters from the same
/// alphabet as a step id: long enough that a project's variables do not collide by accident, and a
/// different length so the two kinds of id are told apart by eye.
/// </summary>
public static class VariableId
{
    /// <summary>The characters an id is built from: no i, l, o, u, 0 or 1.</summary>
    private const string Alphabet = "23456789abcdefghjkmnpqrstvwxyz";

    private const int Length = 6;

    /// <summary>True for an id this program could have written, and only for one of those.</summary>
    public static bool IsWellFormed(string? id)
        => id is { Length: Length } && id.All(character => Alphabet.Contains(character));

    /// <summary>An id no declaration in this scope is using yet.</summary>
    public static string Next(ISet<string> taken)
    {
        for (var attempt = 0; attempt < 64; attempt++)
        {
            var candidate = new string(Random.Shared.GetItems<char>(Alphabet, Length));
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }

        // Sixty-four draws out of a large space all colliding means the set is not what it looks
        // like; walking up from the alphabet is a dull answer that always terminates.
        for (var value = 0; ; value++)
        {
            var candidate = Written(value);
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private static string Written(int value)
    {
        var characters = new char[Length];
        for (var index = 0; index < characters.Length; index++)
        {
            characters[index] = Alphabet[value % Alphabet.Length];
            value /= Alphabet.Length;
        }

        return new string(characters);
    }
}
