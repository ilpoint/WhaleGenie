using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// The status dot is the only shape in the library, and a shape takes its colour from <c>Fill</c>,
/// not from <c>Foreground</c>. Writing <c>Foreground</c> on it compiles, looks right in the file and
/// changes nothing on screen — which is exactly the mistake no comment prevents and no compiler
/// catches. So the fill is read back, and the absence of a foreground is asserted too.
/// </summary>
[Collection("Theme")]
public class ShapeStyleTests
{
    [Fact]
    public void The_status_dot_is_filled_and_leaves_foreground_alone()
    {
        Ui.Run(() =>
        {
            var window = new Window { Width = 200, Height = 200 };
            var dot = new Ellipse();
            window.Content = dot;
            dot.Classes.Add("Dot");
            dot.Classes.Add("Success");

            window.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                Assert.Equal(8, dot.Width);
                Assert.Equal(8, dot.Height);
                Assert.Equal(Token("Success", "SuccessColor"), Colour(dot.Fill));

                // `Foreground` on a shape is silently ignored: asserting it stays unset is the only
                // way to notice that somebody tried.
                Assert.False(
                    dot.IsSet(TextElement.ForegroundProperty),
                    "a foreground was set on a shape, where it does nothing");
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static Color Colour(IBrush? brush)
        => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private static Color Token(string brushKey, string colorKey)
    {
        var variant = Application.Current!.RequestedThemeVariant;
        Assert.True(Application.Current.TryGetResource(brushKey, variant, out var value), brushKey);
        Assert.True(Application.Current.TryGetResource(colorKey, variant, out var colour), colorKey);
        Assert.Equal(Assert.IsType<Color>(colour), Colour(value as IBrush));
        return Assert.IsType<Color>(colour);
    }
}
