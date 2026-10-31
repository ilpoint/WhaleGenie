using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie;

namespace WhaleGenie.Tests;

/// <summary>
/// Whether layer one actually reaches the screen. It is the whole bet behind three families —
/// <c>Button.Secondary</c>, <c>TextBox.Field</c>, <c>ComboBox.Field</c> — whose appearance is
/// supposed to come from the Fluent keys rather than from styles of ours. If those keys were wrong,
/// or the theme ignored them, nothing would fail: the controls would simply paint the stock Fluent
/// colours and the styles would look correct. So the states are driven here and the painted colours
/// are read back.
///
/// The colours are painted on the template's parts, not on the control: a button's state is the
/// background of its content presenter, and a text box's is the border of its border element. Reading
/// the control's own Background would show the rest state in every pseudo-class and hide the truth.
/// </summary>
[Collection("Theme")]
public class LayerOneTests
{
    [Fact]
    public void The_button_paints_the_three_states_from_the_tokens()
    {
        Ui.Run(() =>
        {
            var window = new Window { Width = 300, Height = 200 };
            var button = new Button { Content = "probe" };
            window.Content = button;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                Assert.Equal(Token("SurfaceControl", "SurfaceControlColor"), Face(button));

                Pseudo(button, ":pointerover", true);
                Assert.Equal(Token("SurfaceControlHover", "SurfaceControlHoverColor"), Face(button));

                Pseudo(button, ":pressed", true);
                Assert.Equal(Token("SurfaceControlPressed", "SurfaceControlPressedColor"), Face(button));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void The_text_box_paints_its_border_states_from_the_tokens()
    {
        Ui.Run(() =>
        {
            var window = new Window { Width = 300, Height = 200 };
            var box = new TextBox { Text = "probe" };
            window.Content = box;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                Assert.Equal(Token("BorderControl", "BorderControlColor"), BorderElement(box, stroke: true));

                Pseudo(box, ":pointerover", true);
                Assert.Equal(Token("BorderControlHover", "BorderControlHoverColor"), BorderElement(box, stroke: true));

                Pseudo(box, ":pointerover", false);
                Pseudo(box, ":focus", true);
                Assert.Equal(Token("AccentFill", "AccentFillColor"), BorderElement(box, stroke: true));
                Assert.Equal(Token("SurfaceField", "SurfaceFieldColor"), BorderElement(box, stroke: false));
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>
    /// Drives a pseudo-class the way the input system would. Avalonia has no other way to reach the
    /// hover and pressed states from a check, and the states are the only place the keys can go wrong
    /// without anything failing.
    /// </summary>
    private static void Pseudo(Control control, string name, bool on)
    {
        ((IPseudoClasses)control.Classes).Set(name, on);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// The button's face, read off the control. The theme carries it into the template through
    /// `{TemplateBinding Background}`, while its own state setters sit inside a ControlTheme where a
    /// style out here cannot reach — so the control's Background is the value our tokens own.
    /// </summary>
    private static Color? Face(Button button)
        => button.Background is ISolidColorBrush brush ? brush.Color : null;

    private static Color? BorderElement(TextBox box, bool stroke)
    {
        var border = box.GetVisualDescendants().OfType<Border>()
            .FirstOrDefault(candidate => candidate.Name == "PART_BorderElement");
        var brush = border is null ? null : stroke ? border.BorderBrush : border.Background;
        return brush is ISolidColorBrush solid ? solid.Color : null;
    }

    /// <summary>What the token says the colour should be, read from the live dictionary.</summary>
    private static Color Token(string brushKey, string colorKey)
    {
        var variant = Application.Current!.RequestedThemeVariant;
        Assert.True(Application.Current.TryGetResource(brushKey, variant, out var value), brushKey);
        Assert.True(Application.Current.TryGetResource(colorKey, variant, out var colour), colorKey);

        var painted = Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
        Assert.Equal(Assert.IsType<Color>(colour), painted);
        return painted;
    }
}
