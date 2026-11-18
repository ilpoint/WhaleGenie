using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;


namespace WhaleGenie.ViewModels;

// The rows a value can hold: colour points, regions, key presses and pictures, and how they are numbered.
public partial class StepParameterViewModel : ViewModelBase
{
    /// <summary>Puts another colour at the end of the pattern, for the user to fill in.</summary>
    public void AddColourPoint(ColourPointRowViewModel? row = null)
    {
        var added = row ?? new ColourPointRowViewModel();
        added.Take = RemoveColourPoint;
        added.PropertyChanged += OnColourPointChanged;
        ColourPointRows.Add(added);
        AnnounceColourPoints();
    }

    /// <summary>Takes one colour out of the pattern.</summary>
    public void RemoveColourPoint(ColourPointRowViewModel row)
    {
        row.PropertyChanged -= OnColourPointChanged;
        ColourPointRows.Remove(row);
        AnnounceColourPoints();
    }

    private void OnColourPointChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        => AnnounceColourPoints();

    private void AnnounceColourPoints()
    {
        OnPropertyChanged(nameof(HasColourPoints));
        OnPropertyChanged(nameof(IsMissing));
        OnPropertyChanged(nameof(IsDefault));
    }

    /// <summary>Puts another colour at the end of the list, for the user to fill in.</summary>
    public void AddColourRow(ColourRowViewModel? row = null)
    {
        var added = row ?? new ColourRowViewModel();
        added.Take = RemoveColourRow;
        added.PropertyChanged += OnColourRowChanged;
        ColourRows.Add(added);
        AnnounceColours();
    }

    /// <summary>Takes one colour out of the list.</summary>
    public void RemoveColourRow(ColourRowViewModel row)
    {
        row.PropertyChanged -= OnColourRowChanged;
        ColourRows.Remove(row);
        AnnounceColours();
    }

    private void OnColourRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        => AnnounceColours();

    private void AnnounceColours()
    {
        OnPropertyChanged(nameof(HasColours));
        OnPropertyChanged(nameof(DisplayLine));
        OnPropertyChanged(nameof(IsMissing));
        OnPropertyChanged(nameof(IsDefault));
    }

    /// <summary>Puts a place to look at at the end of the list, for the user to fill in.</summary>
    public void AddRegion(RegionRowViewModel? row = null)
    {
        var added = row ?? new RegionRowViewModel();
        added.Prepare(Variables);
        added.Take = RemoveRegion;
        added.PropertyChanged += OnRegionChanged;
        Regions.Add(added);
        OnPropertyChanged(nameof(HasRegions));
    }

    /// <summary>Takes one place to look at out of the list.</summary>
    public void RemoveRegion(RegionRowViewModel row)
    {
        row.PropertyChanged -= OnRegionChanged;
        Regions.Remove(row);
        OnPropertyChanged(nameof(HasRegions));
    }

    /// <summary>Puts another press at the end of the run.</summary>
    public void AddKeyRow(KeyRowViewModel? row = null)
    {
        var added = row ?? new KeyRowViewModel();
        added.Take = RemoveKeyRow;
        added.PropertyChanged += OnKeyRowChanged;
        KeyRows.Add(added);
        Number(KeyRows);
        OnPropertyChanged(nameof(HasKeyRows));
    }

    /// <summary>Takes one press out of the run.</summary>
    public void RemoveKeyRow(KeyRowViewModel row)
    {
        row.PropertyChanged -= OnKeyRowChanged;
        KeyRows.Remove(row);
        Number(KeyRows);
        OnPropertyChanged(nameof(HasKeyRows));
    }

    /// <summary>Numbers the presses of a run from one, which is what the keyboard calls them.</summary>
    private static void Number(IReadOnlyList<KeyRowViewModel> rows)
    {
        for (var index = 0; index < rows.Count; index++)
        {
            rows[index].Number = index + 1;
        }
    }

    /// <summary>
    /// What one press of the run says is what this field says. The dialog follows the field rather
    /// than its rows, so a key written into a row has to be reported as a change here — otherwise a
    /// run that is filled in goes on being called empty, and the step cannot be saved.
    /// </summary>
    private void OnKeyRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsMissing));
        OnPropertyChanged(nameof(IsIncluded));
    }

    /// <summary>The same for a place to look at: the four numbers typed into a row are this field's value.</summary>
    private void OnRegionChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsMissing));
        OnPropertyChanged(nameof(IsIncluded));
    }
}
