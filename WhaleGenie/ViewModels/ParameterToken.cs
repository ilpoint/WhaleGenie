using System.Collections.Generic;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.ViewModels;

/// <summary>
/// One piece of a value as the field shows it while nobody is typing in it: a run of text, or a
/// name standing between two runs.
///
/// <para>
/// A name is drawn as a box of its own, so what tells a person that this word is a variable is the
/// shape of it rather than a character in front of it. Nothing has to be typed to make one, and
/// nothing has to be escaped to write about one.
/// </para>
/// </summary>
public sealed class ParameterToken
{
    public required string Text { get; init; }

    /// <summary>True for a name, false for a run of text.</summary>
    public required bool IsName { get; init; }

    /// <summary>The other side of <see cref="IsName"/>, for a template that only needs a visibility.</summary>
    public bool IsText => !IsName;

    /// <summary>
    /// The pieces a shape is shown as. A run is its parts one after another; anything else — a
    /// formula, a formula's result, a list — is one piece said in words, because a box holding a
    /// formula is not a sentence with names in it.
    /// </summary>
    public static IReadOnlyList<ParameterToken> Of(ExpressionNode? value)
    {
        var tokens = new List<ParameterToken>();
        switch (value)
        {
            case null:
            case TextNode { Text: "" }:
                return tokens;

            case TextNode text:
                tokens.Add(new ParameterToken { Text = text.Text, IsName = false });
                return tokens;

            case VariableNode name:
                tokens.Add(new ParameterToken { Text = name.Name, IsName = true });
                return tokens;

            case ConcatNode run:
                foreach (var part in run.Parts)
                {
                    tokens.AddRange(Of(part));
                }

                return tokens;

            default:
                tokens.Add(new ParameterToken { Text = Formula.Speak(value), IsName = false });
                return tokens;
        }
    }
}
