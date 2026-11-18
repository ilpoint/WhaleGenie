using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// The one window that fills a cell. The band is a run of pieces — values, signs, brackets, and calls
/// that hold further runs — so the model is judged on the things that run has to promise: everything
/// written is a piece you can see and take away, a call keeps the places it still asks for, and what
/// is handed back is exactly the text the pieces add up to.
/// </summary>
public partial class ValuePickerTests
{
    private static IReadOnlyList<VariableChoice> Variables() =>
    [
        new VariableChoice { Name = "count", Scope = VariableScope.Local, Type = ValueKind.Number },
        new VariableChoice { Name = "title", Scope = VariableScope.Local, Type = ValueKind.Text },
    ];

    /// <summary>
    /// The pieces the band puts at its top level. A whole value has one expression under its root,
    /// so this reads as a run of one; a run field's root is already that run.
    /// </summary>
    private static SlotSequence Root(ValuePickerViewModel form)
    {
        if (form.Recipe is SlotRoot { Child: SlotSequence run })
        {
            return run;
        }

        if (form.Recipe is SlotRoot root)
        {
            return new SlotSequence
            {
                Items = [root.Child],
                Path = NodePath.Root,
                Start = 0,
                Length = root.Child.Length,
            };
        }

        return Assert.IsType<SlotSequence>(form.Recipe);
    }

    /// <summary>
    /// A fixture's way of writing a shape down in one line. The window never sees this string: a
    /// test uses it only to state the shape it wants to open on, which is what the product hands
    /// the window in real life.
    /// </summary>
    public static ExpressionNode Seed(ExpressionNode shape) => shape;
}
