using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
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
                Assert.Equal(Token("AccentFill", "AccentFillColor"), Presenter(primary));
                Assert.Equal(Token("TextOnAccent", "TextOnAccentColor"), PresenterForeground(primary));

                Pseudo(primary, ":pointerover", true);
                Assert.Equal(Token("AccentFillHover", "AccentFillHoverColor"), Presenter(primary));
                Pseudo(primary, ":pointerover", false);

                // The secondary button takes its four states from layer one and only its measurements
                // from the class, so its height is the class's and its background is the theme's.
                var secondary = window.GetVisualDescendants().OfType<Button>()
                    .First(button => button.Classes.Contains("Secondary"));
                Assert.Equal(30, secondary.Height);
                Assert.Equal(Token("SurfaceControl", "SurfaceControlColor"), Presenter(secondary));

                // The window's own close button is a ghost: no fill of its own, a 24 square.
                var close = window.FindControl<Button>("CloseButton");
                Assert.NotNull(close);
                Assert.Equal(24, close!.Width);
                Assert.Equal(Token("TransparentFill", "TransparentFillColor"), Presenter(close));
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

    private static Color? Presenter(Button button)
        => button.GetVisualDescendants().OfType<ContentPresenter>().FirstOrDefault()?.Background is ISolidColorBrush brush
            ? brush.Color
            : null;

    private static Color? PresenterForeground(Button button)
        => button.GetVisualDescendants().OfType<ContentPresenter>().FirstOrDefault()?.Foreground is ISolidColorBrush brush
            ? brush.Color
            : null;

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
