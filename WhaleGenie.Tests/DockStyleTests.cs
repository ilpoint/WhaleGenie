using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace WhaleGenie.Tests;

/// <summary>
/// The dock is the row of category buttons that narrows a wall of things to one group of them: five
/// variable groups beside the macro, four sources in the action library. Its states used to be drawn
/// in two places that disagreed — the library styled its four tabs as plain list rows with a two
/// pixel bar, the wall painted the chosen chip solid accent and left the label in the body colour.
/// Dark on the brand orange is 3.54:1 in the light theme, under the contrast floor the palette
/// promises; on a chip it does not read as "chosen", it reads as "broken".
///
/// So the states are checked here the way a person sees them: the painted background and the
/// effective colour of the label inside the template. The label's colour is the part that used to
/// be impossible to change, because the template had already set it locally.
/// </summary>
[Collection("Theme")]
public class DockStyleTests
{
    [Fact]
    public void The_dock_paints_every_state_and_its_label_follows()
    {
        Ui.Run(() =>
        {
            var list = new ListBox
            {
                Classes = { "Dock" },
                ItemsSource = new[] { "all", "local" },
                ItemTemplate = new FuncDataTemplate<string>((text, _) => new TextBlock { Text = text }),
            };
            var window = new Window { Width = 300, Height = 200, Content = list };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            try
            {
                var item = list.GetVisualDescendants().OfType<ListBoxItem>().First();
                var presenter = item.GetVisualDescendants().OfType<ContentPresenter>().First();
                var label = item.GetVisualDescendants().OfType<TextBlock>().First();

                // At rest the chip is a word on the panel, not a grey button: the dock stands on
                // several surfaces, so it may not bring a surface of its own.
                Assert.Equal(Colors.Transparent, Colour(presenter.Background));
                Assert.Equal(Token("TextSecondary", window), Colour(label.Foreground));

                var restingBorder = presenter.BorderThickness;

                Pseudo(item, ":pointerover", true);
                Assert.Equal(Token("OverlayHover", window), Colour(presenter.Background));
                Assert.Equal(Token("TextPrimary", window), Colour(label.Foreground));
                Pseudo(item, ":pointerover", false);

                Pseudo(item, ":pressed", true);
                Assert.Equal(Token("OverlayPressed", window), Colour(presenter.Background));
                Pseudo(item, ":pressed", false);

                // Chosen: accent surface behind, accent words on top, and a border because a state
                // that is only two colours is also only one signal. The border is the same width at
                // rest, painted transparent — picking a category must not move its neighbours.
                Pseudo(item, ":selected", true);
                Assert.Equal(Token("AccentSurface", window), Colour(presenter.Background));
                Assert.Equal(Token("AccentText", window), Colour(label.Foreground));
                Assert.Equal(Token("AccentText", window), Colour(presenter.BorderBrush));
                Assert.Equal(restingBorder, presenter.BorderThickness);
                Pseudo(item, ":selected", false);

                item.IsEnabled = false;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(Token("TextDisabled", window), Colour(label.Foreground));
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>
    /// The chosen chip's two colours have to clear the contrast floor in both variants. They are
    /// read from the dictionary rather than written here, so a retuned accent cannot quietly fall
    /// under the line.
    /// </summary>
    [Fact]
    public void The_chosen_chip_keeps_its_contrast_in_both_themes()
    {
        Ui.Run(() =>
        {
            foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
            {
                var text = Token("AccentText", variant);
                var surface = Token("AccentSurface", variant);
                Assert.True(Contrast(text, surface) >= 4.5,
                    $"{text} on {surface} is {Contrast(text, surface):0.00}:1 in {variant}");
            }
        });
    }

    private static void Pseudo(Control control, string name, bool on)
    {
        ((IPseudoClasses)control.Classes).Set(name, on);
        Dispatcher.UIThread.RunJobs();
    }

    private static Color Token(string key, StyledElement owner)
        => Token(key, owner.ActualThemeVariant);

    private static Color Token(string key, ThemeVariant variant)
    {
        Assert.True(Application.Current!.TryGetResource(key, variant, out var value), key);
        return Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
    }

    private static Color? Colour(IBrush? brush) => (brush as ISolidColorBrush)?.Color;

    /// <summary>WCAG relative contrast: the same arithmetic the palette's own floor uses.</summary>
    private static double Contrast(Color first, Color second)
    {
        var one = Luminance(first);
        var two = Luminance(second);
        return (System.Math.Max(one, two) + 0.05) / (System.Math.Min(one, two) + 0.05);
    }

    private static double Luminance(Color colour)
    {
        static double Channel(byte value)
        {
            var scaled = value / 255.0;
            return scaled <= 0.03928 ? scaled / 12.92 : System.Math.Pow((scaled + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(colour.R)) + (0.7152 * Channel(colour.G)) + (0.0722 * Channel(colour.B));
    }
}
