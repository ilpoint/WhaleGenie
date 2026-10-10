using System;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Models;

namespace WhaleGenie.ViewModels;

/// <summary>
/// One press of a key run, as the dialog edits it: the combination this press sends, and — where
/// the beat differs from the rest of the run — how long it is held and how long to wait after it.
/// The two numbers are left empty in the ordinary case, so a row reads as the keys it presses.
/// </summary>
public partial class KeyRowViewModel : ViewModelBase
{
    /// <summary>The combination this press sends, written the way a combination always is.</summary>
    [ObservableProperty]
    public partial string Keys { get; set; } = string.Empty;

    /// <summary>How long this combination stays down, or nothing for the run's own hold.</summary>
    [ObservableProperty]
    public partial decimal? HoldMs { get; set; }

    /// <summary>How long to wait after this press, or nothing for the run's own gap.</summary>
    [ObservableProperty]
    public partial decimal? GapMs { get; set; }

    /// <summary>
    /// What takes this row out of the run it is in. The run belongs to the parameter, so the row is
    /// told what to call rather than knowing where it is.
    /// </summary>
    public Action<KeyRowViewModel>? Take { get; set; }

    /// <summary>Adds a key the keyboard handed back to this row's combination.</summary>
    public void AddKey(string key)
    {
        var held = Keys.Trim();
        Keys = held.Length == 0 ? key : held + "+" + key;
    }

    /// <summary>Takes this press out of the run.</summary>
    [RelayCommand]
    private void Remove() => Take?.Invoke(this);

    /// <summary>This row the way the step stores it.</summary>
    public StepParameterRow ToRow()
    {
        var row = new StepParameterRow();
        var keys = Keys.Trim();
        if (keys.Length > 0)
        {
            row.Columns["keys"] = keys;
        }

        Keep(row, "holdMs", HoldMs);
        Keep(row, "gapMs", GapMs);
        return row;
    }

    /// <summary>One press back out of the macro file.</summary>
    public static KeyRowViewModel From(StepParameterRow row) => new()
    {
        Keys = row.Text("keys"),
        HoldMs = Number(row, "holdMs"),
        GapMs = Number(row, "gapMs"),
    };

    private static void Keep(StepParameterRow row, string column, decimal? value)
    {
        if (value is not null)
        {
            row.Columns[column] = value.Value.ToString(CultureInfo.InvariantCulture);
        }
    }

    private static decimal? Number(StepParameterRow row, string column)
        => decimal.TryParse(row.Text(column), NumberStyles.Number, CultureInfo.InvariantCulture,
            out var number)
                ? number
                : null;
}
