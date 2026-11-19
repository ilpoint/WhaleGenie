using System;
using System.Collections;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Windowing;

namespace WhaleGenie.Views;

/// <summary>
/// Every window in WhaleGenie is one of these: FluentAvalonia's <see cref="FAAppWindow"/>, so the
/// title bar, the caption buttons and the rounded frame come from the library rather than from XAML
/// we maintain ourselves.
/// </summary>
public class AppWindow : FAAppWindow
{
    public AppWindow()
    {
        // The bar is the only place a window can be dragged by, so it is taller than the library's
        // 32: at that height the strip reads as an edge rather than as something to grab.
        TitleBar.Height = 40;
        ApplyCaptionButtonColors();

        // The caption buttons belong to the system, but their colours are ours: without this they
        // keep the system's own hover grey, which is a shade off `SurfaceControlHover` and does not
        // change with the light theme. A window's colours are tokens, and the title bar is part of
        // the window.
        ActualThemeVariantChanged += (_, _) => ApplyCaptionButtonColors();
        ForgetHandlelessWindow();
    }

    /// <summary>
    /// Paints the system caption buttons with the same tokens as the buttons in the title bar, so a
    /// pin sitting beside minimize, maximize and close shares their rest, hover and pressed greys.
    /// The close button keeps the system's red — that hover says "this closes the window", not
    /// "this is a button".
    /// </summary>
    /// <remarks>
    /// The colours are plain CLR properties on the library's title bar, not styled properties, so
    /// they take a value rather than a <c>DynamicResource</c>: the theme's tokens are read here and
    /// read again when the window's variant changes. A missing token is passed on as no colour
    /// rather than hidden behind a private fallback; the token checks are what keep it from missing.
    /// </remarks>
    private void ApplyCaptionButtonColors()
    {
        TitleBar.ButtonBackgroundColor = Colors.Transparent;
        TitleBar.ButtonHoverBackgroundColor = CaptionColor("SurfaceControlHoverColor");
        TitleBar.ButtonPressedBackgroundColor = CaptionColor("SurfaceControlPressedColor");
        TitleBar.ButtonForegroundColor = CaptionColor("TextPrimaryColor");
        TitleBar.ButtonHoverForegroundColor = CaptionColor("TextPrimaryColor");
        TitleBar.ButtonPressedForegroundColor = CaptionColor("TextPrimaryColor");
        TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

        // The bar's own title and whale are custom-drawn and stay at TextPrimary when the window is
        // inactive, so the system glyphs keep that colour too. Dimming only the three system buttons
        // would leave the pin beside them looking lit while the rest of the bar is quiet.
        TitleBar.ButtonInactiveForegroundColor = CaptionColor("TextPrimaryColor");
    }

    private Color? CaptionColor(string key)
        => Application.Current is { } app
            && app.TryGetResource(key, ActualThemeVariant, out var found)
            && found is Color color
                ? color
                : null;

    /// <summary>
    /// Drags the window by its own title bar when the content stands in the bar's place.
    /// </summary>
    /// <remarks>
    /// `FAAppWindow.TitleBar.ExtendsContentIntoTitleBar` hands the bar over to the content and stops
    /// moving the window itself — the library exposes no way to say where the draggable part is
    /// (measured: it has a drag rectangle for the default bar and no API to set one). So the strip is
    /// dragged here: a press inside the bar's height moves the window, unless it landed on something
    /// that is there to be clicked.
    /// </remarks>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!TitleBar.ExtendsContentIntoTitleBar
            || WindowState == WindowState.FullScreen
            || e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed
            || e.GetPosition(this).Y > TitleBar.Height
            || OverAControl(e.Source as Visual))
        {
            return;
        }

        BeginMoveDrag(e);
    }

    /// <summary>
    /// Whether the press landed on something that does its own work with a click, rather than on the
    /// bare strip — a button, a field, a thumb. Walking up from the source is what finds the one that
    /// would have handled it.
    /// </summary>
    private bool OverAControl(Visual? source)
    {
        for (var at = source; at is not null && at != this; at = at.GetVisualParent())
        {
            if (at is Button or TextBox or ComboBox or CheckBox or Slider or ScrollBar
                or Thumb or ScrollViewer)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Lets go of the library's window-manager entry when this window has no native handle.
    /// </summary>
    /// <remarks>
    /// <see cref="FAAppWindow"/> keeps a static table of window managers keyed by `HWND`. A headless
    /// session hands out `0` for every window, so the first window takes the only slot and the second
    /// one throws "an item with the same key has already been added" from its constructor — which is
    /// why the checks could never open two of these. A handle of zero means there is no window
    /// underneath at all, and nothing in that table can be reached, so it is emptied and the next
    /// window gets its turn. A real `HWND` is never zero, so on a real machine this never runs and
    /// the library keeps the entry it needs for the title-bar menu.
    ///
    /// The field is reached by name because the type is internal; if a later version renames it,
    /// nothing happens here, which leaves the same "no handle, no table" case this check is in.
    /// </remarks>
    private void ForgetHandlelessWindow()
    {
        if (TryGetPlatformHandle()?.Handle is not { } handle || handle != IntPtr.Zero)
        {
            return;
        }

        var manager = typeof(FAAppWindow).Assembly
            .GetType("FluentAvalonia.UI.Windowing.Win32WindowManager");
        var registry = manager
            ?.GetField("_appWindowRegistry", BindingFlags.NonPublic | BindingFlags.Static)
            ?.GetValue(null);

        (registry as IDictionary)?.Clear();
    }
}
