using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using WhaleGenie.Localization;

namespace WhaleGenie.Views;

/// <summary>
/// A keyboard drawn on screen, so a key is pointed at rather than remembered. What a key is called
/// is a name the macro has to spell exactly, and the spelling of a key is not something anyone
/// carries in their head — the number pad and the navigation block least of all, which is where a
/// bare text field gives the least help.
/// </summary>
/// <remarks>
/// The caps are named the way the step writes them, so what is read here is what the macro stores.
/// The layout is a keyboard's, not a list: a key is found by where it sits. Escape backs out of
/// the picker, and a click on the cap marked Esc takes that key instead, which is why the two are
/// told apart rather than both meaning "cancel".
/// </remarks>
public partial class VirtualKeyboardWindow : Window
{
    private bool _done;

    public VirtualKeyboardWindow()
    {
        InitializeComponent();

        Title = Strings.Get("KeyPad.Title");

        var closeButton = this.FindControl<Button>("CloseButton");
        if (closeButton is not null)
        {
            closeButton.Click += (_, _) => Take(null);
        }

        // Tunnelling, so Escape is caught before a focused key consumes it.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>The name of the key that was clicked, or null when the picker was left alone.</summary>
    internal string? Chosen { get; private set; }

    /// <summary>Shows the keyboard over <paramref name="owner"/> and reports the key picked.</summary>
    public static Task<string?> PickAsync(Window owner)
        => new VirtualKeyboardWindow().ShowDialogOver<string?>(owner);

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // Wired up once the keyboard is really on screen, which is when its caps are there to walk.
        foreach (var cap in this.GetVisualDescendants().OfType<Button>())
        {
            if (cap.Tag is string)
            {
                cap.Click += OnKey;
            }
        }
    }

    /// <summary>Keeps the key whose cap carries this name, which is what every cap is for.</summary>
    private void OnKey(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: string name })
        {
            Take(name);
        }
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not Key.Escape)
        {
            return;
        }

        // The key pressed to get out of the picker is not the key being picked: taking Esc is done
        // by clicking the cap that says so.
        e.Handled = true;
        Take(null);
    }

    private void Take(string? name)
    {
        if (_done)
        {
            return;
        }

        _done = true;
        Chosen = name;
        Close(name);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            && e.GetPosition(this).Y <= 38)
        {
            BeginMoveDrag(e);
        }
    }
}
