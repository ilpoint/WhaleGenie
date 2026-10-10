using System;
using System.Linq;
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
/// The layout is a keyboard's, not a list: a key is found by where it sits. Escape closes the
/// window, and a click on the cap marked Esc writes that key instead, which is why the two are told
/// apart rather than both meaning "cancel". The window stays up and hands over one key at a time,
/// because a combination is built a key at a time: a picker that closed on the first cap would mean
/// opening it again for every key after it.
/// </remarks>
public partial class VirtualKeyboardWindow : Window
{
    public VirtualKeyboardWindow()
    {
        InitializeComponent();

        Title = Strings.Get("KeyPad.Title");

        var closeButton = this.FindControl<Button>("CloseButton");
        if (closeButton is not null)
        {
            closeButton.Click += (_, _) => Close();
        }

        // The keyboard covers the picture the keys are being read off, so it is put away on its own
        // rather than taken down: the keys it has already written stay where they are.
        var minimizeButton = this.FindControl<Button>("MinimizeButton");
        if (minimizeButton is not null)
        {
            minimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;
        }

        var doneButton = this.FindControl<Button>("DoneButton");
        if (doneButton is not null)
        {
            doneButton.Click += (_, _) => Close();
        }

        // Tunnelling, so Escape is caught before a focused key consumes it.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>Raised with the name of each key whose cap is clicked, while the window stays open.</summary>
    public event Action<string>? KeyChosen;

    /// <summary>
    /// What the keys are being written into, said on the window. A keyboard that keeps taking clicks
    /// has to say where they are going, or the user has to remember which field asked for it.
    /// </summary>
    public string Filling
    {
        get => _filling;
        set
        {
            _filling = value;
            if (this.FindControl<TextBlock>("FillingLine") is { } line)
            {
                line.Text = Strings.Format("KeyPad.Filling", value);
            }
        }
    }

    private string _filling = string.Empty;

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
            KeyChosen?.Invoke(name);
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
        Close();
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
