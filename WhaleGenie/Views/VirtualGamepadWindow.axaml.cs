using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;

namespace WhaleGenie.Views;

/// <summary>Which of a controller's controls a step is asking for.</summary>
internal enum GamepadPick
{
    /// <summary>A button to press, which is any control that has a name of its own.</summary>
    Button,

    /// <summary>One of the two sticks, which is chosen by clicking the stick itself.</summary>
    Stick,

    /// <summary>One of the two triggers.</summary>
    Trigger,
}

/// <summary>
/// The virtual controller drawn on screen, so the control a step works is pointed at rather than
/// spelled. It is an Xbox 360 pad, which is the pad the program puts on the machine, so what
/// stands where a hand would find it — the stick under the left thumb, the four face buttons under
/// the right — is where the macro's own names for those controls come from.
/// </summary>
/// <remarks>
/// The same pad answers all three questions a gamepad step asks — which button, which stick, which
/// trigger — and which controls can be clicked follows from the question. A stick is not a button,
/// and a step that presses a button has no answer for one, so the controls the question is not
/// about are left out of it rather than clicking through to a value that means nothing there.
/// </remarks>
public partial class VirtualGamepadWindow : Window
{
    /// <summary>The controls each question is about, named the way the step writes them.</summary>
    private static readonly string[] Sticks = ["ls", "rs"];
    private static readonly string[] Triggers = ["lt", "rt"];

    private readonly GamepadPick _pick;
    private bool _done;

    public VirtualGamepadWindow()
        : this(GamepadPick.Button)
    {
    }

    internal VirtualGamepadWindow(GamepadPick pick)
    {
        InitializeComponent();

        _pick = pick;
        Title = Strings.Get("GamepadPad.Title");

        var closeButton = this.FindControl<Button>("CloseButton");
        if (closeButton is not null)
        {
            closeButton.Click += (_, _) => Take(null);
        }

        if (this.FindControl<TextBlock>("Hint") is { } hint)
        {
            hint.Text = Strings.Get(pick switch
            {
                GamepadPick.Stick => "GamepadPad.StickHint",
                GamepadPick.Trigger => "GamepadPad.TriggerHint",
                _ => "GamepadPad.ButtonsHint",
            });
        }

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>The value that was clicked, or null when the picker was left alone.</summary>
    internal string? Chosen { get; private set; }

    /// <summary>
    /// Shows the pad over <paramref name="owner"/> and reports what was clicked: the name of a
    /// button, or the side of the pad a stick or trigger was asked for.
    /// </summary>
    internal static Task<string?> PickAsync(Window owner, GamepadPick pick)
        => new VirtualGamepadWindow(pick).ShowDialogOver<string?>(owner);

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // Wired up once the pad is really on screen, which is when its controls are there to walk.
        // The controls this question is not about are shown but not clickable: the pad keeps its
        // shape, and a click that would mean nothing there has nowhere to land.
        foreach (var control in this.GetVisualDescendants().OfType<Button>())
        {
            if (control.Tag is not string name)
            {
                continue;
            }

            control.IsEnabled = Answer(name) is not null;
            control.Click += OnControl;
        }
    }

    private void OnControl(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: string name } && Answer(name) is { } value)
        {
            Take(value);
        }
    }

    /// <summary>
    /// What clicking a control answers under the question being asked, or null when that control
    /// has nothing to say about it.
    /// </summary>
    private string? Answer(string control) => _pick switch
    {
        GamepadPick.Stick => control is "ls" ? "left" : control is "rs" ? "right" : null,
        GamepadPick.Trigger => control is "lt" ? "left" : control is "rt" ? "right" : null,
        _ => GamepadNames.Button(control) is not null ? control : null,
    };

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not Key.Escape)
        {
            return;
        }

        e.Handled = true;
        Take(null);
    }

    private void Take(string? value)
    {
        if (_done)
        {
            return;
        }

        _done = true;
        Chosen = value;
        Close(value);
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
