using WhaleGenie.Core.Expressions;

namespace WhaleGenie.ViewModels;

/// <summary>
/// What the value window hands back: the shape the person built rather than the line it was
/// written as. The shape carries each name's identity, so a reference picked in the window does
/// not have to be found again by spelling when the field is stored. <paramref name="Chosen"/> is
/// false when the window was left alone, which is different from a cell that was deliberately
/// cleared.
/// </summary>
public sealed record ValuePick(ExpressionNode Value, bool Chosen)
{
    public static ValuePick Cancelled { get; } = new(Formula.Nothing, false);
}
