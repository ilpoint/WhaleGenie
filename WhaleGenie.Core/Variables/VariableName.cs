using System.Linq;

namespace WhaleGenie.Core.Variables;

/// <summary>
/// What a variable may be called. The rule lives here on its own because three things ask it and
/// they have to agree: a declaration that names a variable, a field that writes a name down, and
/// the expression language that reads one back. There is no sign in front of a name — a name is a
/// word, and being a word is the whole of what makes it one.
/// </summary>
public static class VariableName
{
    /// <summary>The namespace the program keeps for the values it provides itself.</summary>
    public const string SystemPrefix = "sys.";

    /// <summary>
    /// True when the text is a name rather than a formula or a sentence. A name may begin with a
    /// digit: a reference is put into a field by the picker rather than typed, so "123" is a name a
    /// step can hold and read even though typing 123 into a formula would be a number.
    /// </summary>
    public static bool IsName(string? text)
        => text is { Length: > 0 } && IsStart(text[0]) && text.All(IsPart);

    /// <summary>The characters a name may begin with.</summary>
    public static bool IsStart(char character)
        => char.IsLetterOrDigit(character) || character == '_';

    /// <summary>The characters that may follow, a dot included: a record's field is read by name.</summary>
    public static bool IsPart(char character)
        => char.IsLetterOrDigit(character) || character is '_' or '.';
}
