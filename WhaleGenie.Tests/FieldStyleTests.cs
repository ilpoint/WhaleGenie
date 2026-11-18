using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// A field that completes as you type is the same field as any other: what paints it is the
/// `TextBox` inside the `AutoCompleteBox`, and the outer control only holds the place for it.
/// </summary>
/// <remarks>
/// Split that wrong — the height set outside, the padding inside — and the inner box is laid out
/// taller than its slot, so its top and bottom borders are clipped away and its fill is the window
/// behind it. What is left on screen is a pair of brackets where a field should be, and no colour is
/// wrong anywhere: no brush check would see it. That is what happened to the breakpoint's condition
/// box, which is why this check measures the two boxes against each other instead of reading a
/// token.
/// </remarks>
public class FieldStyleTests
{
    [Fact]
    public void A_completing_field_is_drawn_inside_the_slot_that_holds_it()
    {
        Ui.Run(() =>
        {
            var window = new BreakpointWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var field = window.FindControl<AutoCompleteBox>("ExpressionBox")!;
            var inner = field.GetVisualDescendants().OfType<TextBox>().Single();

            Assert.True(inner.Bounds.Height > 0 && field.Bounds.Height > 0,
                $"nothing was laid out: field={field.Bounds}, inner={inner.Bounds}");
            Assert.True(inner.Bounds.Height <= field.Bounds.Height,
                $"the box inside the field is taller than the field itself, so its top and bottom "
                + $"borders are clipped: inner={inner.Bounds}, field={field.Bounds}");
            Assert.True(inner.Bounds.Width <= field.Bounds.Width,
                $"the box inside the field is wider than the field itself: inner={inner.Bounds}, "
                + $"field={field.Bounds}");
            Assert.True(inner.BorderThickness.Top > 0 && inner.BorderThickness.Bottom > 0,
                $"the field has no top or bottom border: {inner.BorderThickness}");

            // The fill belongs to the inner box too. Clearing it there (to avoid painting it twice)
            // leaves the window showing through, which is the other half of the same mistake.
            var fill = Assert.IsAssignableFrom<ISolidColorBrush>(inner.Background);
            Assert.NotEqual(Colors.Transparent, fill.Color);

            Ui.CloseAndAnswer(window);
        });
    }
}
