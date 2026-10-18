using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Localization;

namespace WhaleGenie.ViewModels;

/// <summary>One row of the custom palette: a semantic color and the value picked for it.</summary>
public sealed partial class ThemeColorEntryViewModel : ViewModelBase
{
    public required ThemeBrush Brush { get; init; }

    public string Key => Brush.Key;

    public string Label => Strings.Get(Brush.LabelKey);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Hex))]
    [NotifyPropertyChangedFor(nameof(Swatch))]
    private Color _color;

    public string Hex => ThemePalette.ToHex(Color);

    public IBrush Swatch => new SolidColorBrush(Color);

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Raised after the user changes this color, never while the row is being built.</summary>
    public event Action? Changed;

    public void Reset(bool light)
    {
        var text = light ? Brush.Light : Brush.Dark;
        if (ThemePalette.TryParse(text, out var colour))
        {
            Color = colour;
        }
    }

    public override void RefreshLanguage()
    {
        base.RefreshLanguage();
        OnPropertyChanged(nameof(Label));
    }

    partial void OnColorChanged(Color value) => Changed?.Invoke();
}

/// <summary>A heading and its colors in the palette list.</summary>
public sealed class ThemeColorGroupViewModel : ViewModelBase
{
    public required string Key { get; init; }

    public string Label => Strings.Get(Key);

    public required IReadOnlyList<ThemeColorEntryViewModel> Entries { get; init; }

    public override void RefreshLanguage()
    {
        base.RefreshLanguage();
        OnPropertyChanged(nameof(Label));
        foreach (var entry in Entries)
        {
            entry.RefreshLanguage();
        }
    }
}

/// <summary>
/// Backs the custom palette window. Every change is applied at once and saved after the user has
/// stopped moving the picker for a moment, so dragging a slider does not write the settings file
/// on every frame.
/// </summary>
public sealed partial class ThemePaletteViewModel : ViewModelBase
{
    private readonly DispatcherTimer _saveTimer;
    private bool _ready;

    public ThemePaletteViewModel()
        : this(ThemeController.Load())
    {
    }

    public ThemePaletteViewModel(ThemeSelection selection)
    {
        BaseLight = selection.Mode is ThemeMode.Custom ? selection.CustomBaseLight : selection.BaseLight;

        var colors = selection.Mode is ThemeMode.Custom && selection.CustomColors.Count > 0
            ? selection.CustomColors
            : ThemePalette.Defaults(BaseLight);
        var current = new ThemeSelection(ThemeMode.Custom, BaseLight, colors);

        var entries = new List<ThemeColorEntryViewModel>();
        foreach (var brush in ThemePalette.Brushes)
        {
            var entry = new ThemeColorEntryViewModel
            {
                Brush = brush,
                Color = ThemePalette.Resolve(brush, current),
            };
            entry.Changed += OnEntryChanged;
            entries.Add(entry);
        }

        Entries = entries;
        Groups = [.. entries
            .GroupBy(entry => entry.Brush.GroupKey)
            .Select(group => new ThemeColorGroupViewModel
            {
                Key = group.Key,
                Entries = [.. group],
            })];
        SelectedEntry = entries[0];
        SelectedEntry.IsSelected = true;

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _saveTimer.Tick += (_, _) => Flush();
        _ready = true;
    }

    public IReadOnlyList<ThemeColorEntryViewModel> Entries { get; }

    public IReadOnlyList<ThemeColorGroupViewModel> Groups { get; }

    [ObservableProperty]
    public partial ThemeColorEntryViewModel SelectedEntry { get; set; }

    /// <summary>Whether the controls around the colors use the dark or the light Fluent variant.</summary>
    [ObservableProperty]
    public partial bool BaseLight { get; set; }

    /// <summary>Moves the editor to a row clicked in the palette list.</summary>
    public void Select(ThemeColorEntryViewModel entry)
    {
        if (ReferenceEquals(entry, SelectedEntry))
        {
            return;
        }

        SelectedEntry.IsSelected = false;
        SelectedEntry = entry;
        SelectedEntry.IsSelected = true;
    }

    /// <summary>Applies every row and the base variant to the running program.</summary>
    public void Apply()
    {
        if (!_ready)
        {
            return;
        }

        ThemeController.Apply(Selection());
    }

    /// <summary>Writes the current custom palette, whether or not the debounce is still pending.</summary>
    public void Flush()
    {
        _saveTimer.Stop();
        if (_ready)
        {
            ThemeController.Save(Selection());
        }
    }

    [RelayCommand]
    private void ResetSelected()
    {
        SelectedEntry.Reset(BaseLight);
        Apply();
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    [RelayCommand]
    private void ResetAll()
    {
        foreach (var entry in Entries)
        {
            entry.Reset(BaseLight);
        }

        Apply();
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public override void RefreshLanguage()
    {
        base.RefreshLanguage();
        foreach (var group in Groups)
        {
            group.RefreshLanguage();
        }
    }

    partial void OnBaseLightChanged(bool value)
    {
        Apply();
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void OnEntryChanged()
    {
        Apply();
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private ThemeSelection Selection()
        => new(ThemeMode.Custom, BaseLight,
            Entries.ToDictionary(entry => entry.Key, entry => entry.Hex, StringComparer.OrdinalIgnoreCase));
}
