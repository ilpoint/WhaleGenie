using System;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WhaleGenie.ViewModels;

/// <summary>
/// Backs the accent picker. One colour is the whole choice: the buttons, the selected rows and the
/// accent text are derived from it, and each change is applied at once and saved after the user has
/// stopped moving the picker for a moment, so dragging a slider does not write the settings file on
/// every frame.
/// </summary>
public sealed partial class ThemePaletteViewModel : ViewModelBase
{
    private readonly DispatcherTimer _saveTimer;
    private bool _ready;

    /// <summary>The five colours in use: the picked one, or the built-in accent when it was refused.</summary>
    private AccentTokens _tokens;

    private IBrush _fillPreview = Brushes.Transparent;
    private IBrush _surfacePreview = Brushes.Transparent;
    private IBrush _textPreview = Brushes.Transparent;

    public ThemePaletteViewModel()
        : this(ThemeController.Load())
    {
    }

    public ThemePaletteViewModel(ThemeSelection selection)
    {
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _saveTimer.Tick += (_, _) => Flush();

        BaseLight = selection.BaseLight;
        Accent = ThemePalette.TryParse(selection.AccentHex, out var accent) ? accent : BuiltInAccent;
        Derive();

        _ready = true;
    }

    private static Color BuiltInAccent => Color.Parse(ThemePalette.DefaultAccent);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccentHex))]
    private Color _accent = BuiltInAccent;

    /// <summary>The picked colour as it is written to the settings file and shown beside the picker.</summary>
    public string AccentHex => ThemePalette.ToHex(Accent);

    /// <summary>Whether the controls around the accent use the dark or the light Fluent variant.</summary>
    [ObservableProperty]
    private bool _baseLight;

    /// <summary>
    /// False when the picked colour could not be made readable. The previews then show the built-in
    /// accent, which is what the program is actually painting with, and the window says why.
    /// </summary>
    public bool AccentUsable { get; private set; }

    /// <summary>A primary button: what a filled button will look like.</summary>
    public IBrush FillPreview
    {
        get => _fillPreview;
        private set
        {
            _fillPreview = value;
            OnPropertyChanged();
        }
    }

    /// <summary>A selected row: the accent laid faintly over the window.</summary>
    public IBrush SurfacePreview
    {
        get => _surfacePreview;
        private set
        {
            _surfacePreview = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Accent text standing on the window surface.</summary>
    public IBrush TextPreview
    {
        get => _textPreview;
        private set
        {
            _textPreview = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Applies the choice to the running program.</summary>
    public void Apply()
    {
        if (_ready)
        {
            ThemeController.Apply(Selection());
        }
    }

    /// <summary>Writes the choice, whether or not the debounce is still pending.</summary>
    public void Flush()
    {
        _saveTimer.Stop();
        if (_ready)
        {
            ThemeController.Save(Selection());
        }
    }

    [RelayCommand]
    private void ResetAccent()
    {
        Accent = BuiltInAccent;
        Apply();
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    partial void OnAccentChanged(Color value)
    {
        Derive();
        Apply();
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    partial void OnBaseLightChanged(bool value)
    {
        Derive();
        Apply();
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>
    /// Reads the picked colour into the five the previews paint with. A colour the derivation
    /// refuses is shown as the built-in accent rather than as itself: the window has to show what
    /// the program will do, and it will not paint with a colour white text cannot be read on.
    /// </summary>
    private void Derive()
    {
        AccentUsable = ThemePalette.TryDerive(Accent, BaseLight, out var picked);
        if (!AccentUsable && !ThemePalette.TryDerive(BuiltInAccent, BaseLight, out picked))
        {
            picked = default;
        }

        _tokens = picked;
        FillPreview = new SolidColorBrush(_tokens.Fill);
        SurfacePreview = new SolidColorBrush(_tokens.Surface);
        TextPreview = new SolidColorBrush(_tokens.Text);
        OnPropertyChanged(nameof(AccentUsable));
    }

    private ThemeSelection Selection()
        => new(ThemeMode.Custom, BaseLight, AccentHex);
}
