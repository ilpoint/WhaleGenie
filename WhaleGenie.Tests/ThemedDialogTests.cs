using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The pilot window for layer three. Its styles used to be four private copies inside the window —
/// its own button colours, its own corner radius, its own font size — which is exactly what the
/// palette work is meant to end. What it paints now has to come from the tokens, and the states of
/// the two kinds of button have to come from the classes rather than from the window.
/// </summary>
[Collection("Theme")]
public class ThemedDialogTests
{
    [Fact]
    public void The_dialog_paints_itself_from_the_tokens()
    {
        Ui.Run(() =>
        {
            var window = new ConfirmDialog(new ConfirmDialogViewModel
            {
                Header = "header",
                Message = "message",
                PrimaryLabel = "yes",
                SecondaryLabel = "no",
            });
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                Assert.Equal(Token("SurfaceWindow", "SurfaceWindowColor"), Colour(window.Background));

                var primary = window.GetVisualDescendants().OfType<Button>()
                    .First(button => button.Classes.Contains("Primary"));
                Assert.Equal(32, primary.Height);
                Assert.Equal(Token("AccentFill", "AccentFillColor"), Face(primary));
                Assert.Equal(Token("TextOnAccent", "TextOnAccentColor"), Label(primary));

                Pseudo(primary, ":pointerover", true);
                Assert.Equal(Token("AccentFillHover", "AccentFillHoverColor"), Face(primary));
                Pseudo(primary, ":pointerover", false);

                // The secondary button takes its four states from layer one and only its measurements
                // from the class, so its height is the class's and its background is the theme's.
                var secondary = window.GetVisualDescendants().OfType<Button>()
                    .First(button => button.Classes.Contains("Secondary"));
                Assert.Equal(30, secondary.Height);
                Assert.Equal(Token("SurfaceControl", "SurfaceControlColor"), Face(secondary));

                // The message is body copy, not a field label: same 13px and colour, but no bottom
                // margin — a label carries the gap down to the control it names, prose does not.
                var body = window.GetVisualDescendants().OfType<TextBlock>()
                    .First(text => text.Classes.Contains("Body"));
                Assert.Equal(0, body.Margin.Bottom);
                Assert.Equal(Token("TextSecondary", "TextSecondaryColor"), Colour(body.Foreground));
                Assert.Equal(
                    Application.Current!.TryGetResource("UiBodySize", null, out var size) ? size : null,
                    body.FontSize);

                // The close key belongs to the window's title bar, which the library draws: there is
                // no button of ours left to measure here. Its look is a person's job to check on a
                // real machine — in a headless session the library draws no title bar at all.
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static void Pseudo(Control control, string name, bool on)
    {
        ((IPseudoClasses)control.Classes).Set(name, on);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// The button's face and its label, read off the control rather than its template. Under a
    /// ControlTheme the colour arrives in the template through `{TemplateBinding Background}`, while
    /// the theme's own state setters live inside that theme — where a style here cannot reach and a
    /// check here cannot drive them. The control's own values are the ones our tokens set.
    /// </summary>
    private static Color? Face(Button button) => Colour(button.Background);

    private static Color? Label(Button button) => Colour(button.Foreground);

    private static Color? Colour(IBrush? brush)
        => brush is ISolidColorBrush solid ? solid.Color : null;

    private static Color Token(string brushKey, string colorKey)
    {
        var variant = Application.Current!.RequestedThemeVariant;
        Assert.True(Application.Current.TryGetResource(brushKey, variant, out var value), brushKey);
        Assert.True(Application.Current.TryGetResource(colorKey, variant, out var colour), colorKey);
        Assert.Equal(Assert.IsType<Color>(colour), Assert.IsAssignableFrom<ISolidColorBrush>(value).Color);
        return Assert.IsType<Color>(colour);
    }
}
