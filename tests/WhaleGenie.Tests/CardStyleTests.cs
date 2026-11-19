using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace WhaleGenie.Tests;

/// <summary>
/// The selectable card carries two states at once without either one taking the other's place: the
/// background says "this one is selected", the border says "this one has a problem". Both have to be
/// true together, so the checks drive them in every combination — including hovering a selected card,
/// which is where a plain `:pointerover` would quietly win over the selection.
/// </summary>
[Collection("Theme")]
public class CardStyleTests
{
    [Fact]
    public void The_card_keeps_selection_and_problem_on_separate_channels()
    {
        Ui.Run(() =>
        {
            var window = new Window { Width = 300, Height = 200 };
            var card = new Border();
            window.Content = card;
            foreach (var name in new[] { "Card", "Selectable" })
            {
                card.Classes.Add(name);
            }

            window.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                Assert.Equal(Token("SurfaceCard", "SurfaceCardColor"), Colour(card.Background));
                Assert.Equal(Token("BorderSubtle", "BorderSubtleColor"), Colour(card.BorderBrush));

                Pseudo(card, ":pointerover", true);
                Assert.Equal(Token("SurfaceHover", "SurfaceHoverColor"), Colour(card.Background));
                Pseudo(card, ":pointerover", false);

                card.Classes.Add("selected");
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(Token("AccentSurface", "AccentSurfaceColor"), Colour(card.Background));
                Assert.Equal(
                    Token("BorderSubtle", "BorderSubtleColor"),
                    Colour(card.BorderBrush));

                // Hovering a selected card must not turn its background back into the hover colour.
                Pseudo(card, ":pointerover", true);
                Assert.Equal(Token("AccentSurface", "AccentSurfaceColor"), Colour(card.Background));
                Pseudo(card, ":pointerover", false);

                card.Classes.Add("problem");
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(Token("Warning", "WarningColor"), Colour(card.BorderBrush));
                Assert.Equal(
                    Token("AccentSurface", "AccentSurfaceColor"),
                    Colour(card.Background));
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
