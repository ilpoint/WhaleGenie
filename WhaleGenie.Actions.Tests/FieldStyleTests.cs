using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// A value field is drawn on its own now rather than as a completing box holding a second box
/// inside it. What used to be a pair of nested heights is one read-only field, so this check
/// measures that field itself: if its border or fill is lost, what is left on screen is a pair of
/// brackets where a field should be, and no brush check on an inner control would see it.
/// </summary>
public class FieldStyleTests
{
    [Fact]
    public void A_read_only_value_field_is_drawn_with_its_border_and_fill()
    {
        Ui.Run(() =>
        {
            var window = new BreakpointWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var field = window.FindControl<TextBox>("ExpressionBox")!;
            Assert.True(field.IsReadOnly, "a value field must not take a typed line");
            Assert.True(field.Bounds.Height > 0 && field.Bounds.Width > 0,
                $"nothing was laid out: field={field.Bounds}");
            Assert.True(field.BorderThickness.Top > 0 && field.BorderThickness.Bottom > 0,
                $"the field has no top or bottom border: {field.BorderThickness}");

            var fill = Assert.IsAssignableFrom<ISolidColorBrush>(field.Background);
            Assert.NotEqual(Colors.Transparent, fill.Color);

            Ui.CloseAndAnswer(window);
        });
    }
}
