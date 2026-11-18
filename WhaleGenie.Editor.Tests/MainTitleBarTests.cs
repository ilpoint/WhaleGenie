using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

public class MainTitleBarTests
{
    /// <summary>The width the system's caption buttons take: three cells of 46 at 100% scaling.</summary>
    private const double CaptionWidth = 138;

    [Fact]
    public void The_always_on_top_button_stays_clear_of_the_system_buttons_at_the_minimum_width()
    {
        Ui.Run(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.Width = window.MinWidth;
            Dispatcher.UIThread.RunJobs();

            var pin = Assert.IsType<Button>(window.FindControl<Button>("AlwaysOnTopButton"));
            var corner = pin.TranslatePoint(new Point(0, 0), window);
            Assert.True(corner is not null, "the pin button is not laid out in the window");
            Assert.True(corner.Value.X + pin.Bounds.Width <= window.ClientSize.Width - CaptionWidth,
                $"pin ends at {corner.Value.X + pin.Bounds.Width}, window is {window.ClientSize.Width} wide");
            window.Close();
        });
    }

    /// <summary>
    /// The pin is a caption button, not a button that happens to sit in the title bar: it has to fill
    /// the cell immediately left of minimize, from the top edge down 32 pixels. Vertically centring it
    /// in the 40-pixel bar is what left a rounded chip floating four pixels below the system buttons.
    /// </summary>
    [Fact]
    public void The_always_on_top_button_fills_the_caption_cell_at_the_top_of_the_bar()
    {
        Ui.Run(() =>
        {
            var window = new MainWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var pin = Assert.IsType<Button>(window.FindControl<Button>("AlwaysOnTopButton"));
            var corner = pin.TranslatePoint(new Point(0, 0), window);
            Assert.True(corner is not null, "the pin button is not laid out in the window");

            Assert.Equal(0d, corner.Value.Y, 3);
            Assert.Equal(46d, pin.Bounds.Width, 3);
            Assert.Equal(32d, pin.Bounds.Height, 3);
            Assert.Equal(new CornerRadius(0), pin.CornerRadius);
            Assert.Equal(
                window.ClientSize.Width - CaptionWidth,
                corner.Value.X + pin.Bounds.Width,
                3);
            window.Close();
        });
    }

    /// <summary>
    /// The run window's pin is the same caption button. Its bar leaves the system's width with a
    /// margin rather than a column, so the alignment is a second place that can drift.
    /// </summary>
    [Fact]
    public void The_run_window_pin_fills_the_caption_cell_at_the_top_of_the_bar()
    {
        Ui.Run(() =>
        {
            var window = new RunWindow([], NullDeviceLayer.Instance);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var pin = Assert.IsType<Button>(window.FindControl<Button>("PinButton"));
            var corner = pin.TranslatePoint(new Point(0, 0), window);
            Assert.True(corner is not null, "the pin button is not laid out in the run window");

            Assert.Equal(0d, corner.Value.Y, 3);
            Assert.Equal(46d, pin.Bounds.Width, 3);
            Assert.Equal(32d, pin.Bounds.Height, 3);
            Assert.Equal(new CornerRadius(0), pin.CornerRadius);
            window.Close();
        });
    }

    /// <summary>
    /// The system draws minimize, maximize and close, so their greys are not in our templates. The
    /// window hands the library the tokens instead; otherwise the OS's own hover grey sits next to
    /// the pin's token grey and the two do not match, and the light theme keeps the dark OS colours.
    /// </summary>
    [Fact]
    public void The_system_caption_buttons_take_the_theme_tokens()
    {
        Ui.Run(() =>
        {
            var window = new MainWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
            {
                window.RequestedThemeVariant = variant;
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(variant, window.ActualThemeVariant);
                Assert.Equal(Colors.Transparent, window.TitleBar.ButtonBackgroundColor);
                Assert.Equal(Token("SurfaceControlHoverColor", variant), window.TitleBar.ButtonHoverBackgroundColor);
                Assert.Equal(Token("SurfaceControlPressedColor", variant), window.TitleBar.ButtonPressedBackgroundColor);
                Assert.Equal(Token("TextPrimaryColor", variant), window.TitleBar.ButtonForegroundColor);
                Assert.Equal(Token("TextPrimaryColor", variant), window.TitleBar.ButtonHoverForegroundColor);
                Assert.Equal(Token("TextPrimaryColor", variant), window.TitleBar.ButtonInactiveForegroundColor);
            }

            window.Close();
        });
    }

    /// <summary>
    /// `pinned` is added after the template exists, so the state is read the way a person sees it:
    /// the painted background of the presenter, and the stroke of the pin inside it. The background
    /// transitions over 83 ms in the library's template, which is why the wait is for the colour
    /// rather than for the click.
    /// </summary>
    [Fact]
    public void Pinning_the_window_fills_the_pin_with_the_accent_surface()
    {
        var answered = Ui.RunAsync(async () =>
        {
            var window = new MainWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var pin = Assert.IsType<Button>(window.FindControl<Button>("AlwaysOnTopButton"));
            var icon = Assert.IsType<IconView>(window.FindControl<IconView>("PinIcon"));
            var presenter = pin.GetVisualDescendants().OfType<ContentPresenter>().Single();

            pin.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Ui.Until(
                () => Colour(presenter.Background) == Token("AccentSurfaceColor", window),
                "the pinned pin never took the accent surface");
            Assert.True(window.Topmost);
            Assert.Equal(Token("AccentTextColor", window), Colour(icon.Stroke));

            pin.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Ui.Until(
                () => Colour(presenter.Background) == Colors.Transparent,
                "the unpinned pin never went back to the bar's background");
            Assert.False(window.Topmost);
            Assert.Equal(Token("TextPrimaryColor", window), Colour(icon.Stroke));

            window.Close();
            return true;
        });

        Assert.True(answered);
    }

    private static Color Token(string key, StyledElement owner)
        => Token(key, owner.ActualThemeVariant);

    private static Color Token(string key, ThemeVariant variant)
    {
        Assert.True(Application.Current!.TryGetResource(key, variant, out var found), key);
        return Assert.IsAssignableFrom<Color>(found);
    }

    private static Color? Colour(IBrush? brush) => (brush as ISolidColorBrush)?.Color;
}
