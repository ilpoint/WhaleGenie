using System;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// The ways a piece of writing is compared with another, read from the word a step wrote. The five
/// ways themselves live in <see cref="TextCompare"/>; what is here is what a mistake means — a word
/// none of the five names is a failed step rather than a quiet fallback to some other comparison —
/// and the three switches a comparison carries.
/// </summary>
internal static class TextRule
{
    /// <summary>One of the five ways, as the step wrote it; nothing written means "contained in it".</summary>
    public static string Way(string written)
    {
        try
        {
            return TextCompare.Read(written);
        }
        catch (ArgumentException)
        {
            throw new StepFailure(RunKeys.BadMatchMode, written.Trim());
        }
    }

    /// <summary>
    /// The switches a comparison carries. Spacing is a question only "exactly" asks by default;
    /// leaving the switch out keeps that default, while writing it false keeps the spaces the text
    /// actually has.
    /// </summary>
    public static TextCompareOptions Options(string way, bool caseSensitive, bool trim,
        bool multiline)
        => new(caseSensitive, way == TextCompare.Exactly ? trim : null, multiline);
}
