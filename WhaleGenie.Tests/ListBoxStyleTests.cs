using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace WhaleGenie.Tests;

/// <summary>
/// The list family paints its states on the template's content presenter, not on the item itself.
/// That is the write-side of the same trap the layer-one checks hit: a setter written on the item
/// compiles, looks right in the file, and changes nothing on screen. So the states are driven here
/// and the painted background is read back.
/// </summary>
[Collection("Theme")]
public class ListBoxStyleTests
{
    [Fact]
    public void The_list_item_paints_hover_and_selected_on_its_presenter()
    {
        Ui.Run(() =>
        {
            var (window, list) = Show(classes: "List");
            try
            {
                var item = list.GetVisualDescendants().OfType<ListBoxItem>().First();
                Assert.Equal(Token("Transparent", null), Presenter(item));

                Pseudo(item, ":pointerover", true);
                Assert.Equal(Token("OverlayHover", "OverlayHoverColor"), Presenter(item));

                Pseudo(item, ":pointerover", false);
                var restLeft = PresenterBorderThickness(item).Left;
                Pseudo(item, ":selected", true);
                Assert.Equal(Token("AccentSurface", "AccentSurfaceColor"), Presenter(item));

                // Selecting must not move the content: the rest state already carries the same
                // border thickness, painted transparent. A difference here is a 2px jump that no
                // screenshot would show.
                Assert.Equal(restLeft, PresenterBorderThickness(item).Left);
                Assert.True(restLeft > 0, "the leading indicator's room is not reserved");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void The_bare_list_paints_nothing_so_the_card_inside_can()
    {
        Ui.Run(() =>
        {
            var (window, list) = Show(classes: "List Bare");
            try
            {
                var item = list.GetVisualDescendants().OfType<ListBoxItem>().First();
                foreach (var state in new[] { ":pointerover", ":pressed", ":selected" })
                {
                    Pseudo(item, state, true);
                    Assert.Equal(Colors.Transparent, Presenter(item));
                    Pseudo(item, state, false);
                }
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static (Window Window, ListBox List) Show(string classes)
    {
        var list = new ListBox { ItemsSource = new[] { "one", "two" } };
        var window = new Window { Width = 300, Height = 200, Content = list };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        foreach (var name in classes.Split(' '))
        {
            list.Classes.Add(name);
        }

        Dispatcher.UIThread.RunJobs();
        return (window, list);
    }

    private static void Pseudo(Control control, string name, bool on)
    {
        ((IPseudoClasses)control.Classes).Set(name, on);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The thickness of the row's border, which is where its leading indicator lives.</summary>
    private static Thickness PresenterBorderThickness(ListBoxItem item)
        => item.GetVisualDescendants().OfType<ContentPresenter>().FirstOrDefault()?.BorderThickness
            ?? default;

    private static Color? Presenter(ListBoxItem item)
        => item.GetVisualDescendants().OfType<ContentPresenter>().FirstOrDefault()?.Background
            is ISolidColorBrush brush
            ? brush.Color
            : null;

    private static Color Token(string brushKey, string? colorKey)
    {
        var variant = Application.Current!.RequestedThemeVariant;
        if (colorKey is null)
        {
            return Colors.Transparent;
        }

        Assert.True(Application.Current.TryGetResource(brushKey, variant, out var value), brushKey);
        Assert.True(Application.Current.TryGetResource(colorKey, variant, out var colour), colorKey);
        Assert.Equal(Assert.IsType<Color>(colour), Assert.IsAssignableFrom<ISolidColorBrush>(value).Color);
        return Assert.IsType<Color>(colour);
    }
}
