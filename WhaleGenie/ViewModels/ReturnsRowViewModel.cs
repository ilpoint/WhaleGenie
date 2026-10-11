using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.ViewModels;

/// <summary>
/// One line of a step's returns page: a result the action can hand over, the box that says whether
/// the macro keeps it, and the name it goes by. Each result is named on its own, so a step that
/// reports a position and a count can be read back as either without the two sharing a name.
/// </summary>
public partial class ReturnsRowViewModel : ViewModelBase
{
    /// <summary>The name the dialog suggested last, so a name the user typed can be told apart.</summary>
    private string _suggested = string.Empty;

    /// <summary>The names this row has to steer clear of, refreshed as the dialog's picture changes.</summary>
    private HashSet<string> _taken;

    public ReturnsRowViewModel(ActionReturn result, IEnumerable<string>? known = null)
    {
        Result = result;
        _taken = new HashSet<string>(known ?? [], StringComparer.OrdinalIgnoreCase);
        Ticked = result.Primary;
    }

    /// <summary>The result this line is about, as the catalogue declares it.</summary>
    public ActionReturn Result { get; }

    public string Key => Result.Key;

    /// <summary>What the result is, in the interface language, which is also part of its name.</summary>
    public string Label => Result.LocalLabel;

    public string Hint => Result.LocalHint;

    public bool HasHint => Hint.Length > 0;

    /// <summary>True for the result this action nearly always hands over, which starts ticked.</summary>
    public bool IsPrimary => Result.Primary;

    /// <summary>True while this result is one the step hands over.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNameTaken))]
    [NotifyPropertyChangedFor(nameof(NameWarning))]
    [NotifyPropertyChangedFor(nameof(ShowsName))]
    public partial bool Ticked { get; set; }

    /// <summary>The name the result is handed over under.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNameTaken))]
    [NotifyPropertyChangedFor(nameof(NameWarning))]
    public partial string Name { get; set; } = string.Empty;

    /// <summary>True once the user has typed a name of their own into this line.</summary>
    public bool IsNameChanged { get; private set; }

    /// <summary>True while the line is worth showing a name for.</summary>
    public bool ShowsName => Ticked || Name.Length > 0;

    /// <summary>
    /// True when the name this result goes by is already in use somewhere the macro can see, which
    /// is how two steps end up writing into one variable.
    /// </summary>
    public bool IsNameTaken => Ticked
        && Name.Trim().Length > 0
        && _taken.Contains(Name.Trim());

    /// <summary>What the line under the name says when it is already taken.</summary>
    public string NameWarning => IsNameTaken
        ? Strings.Format("Add.NameTaken", Name.Trim())
        : string.Empty;

    /// <summary>
    /// Tells this line which names are spoken for, which is every variable the macro already has
    /// and the results of the lines above it.
    /// </summary>
    public void Knows(IEnumerable<string> taken)
    {
        _taken = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
        OnPropertyChanged(nameof(IsNameTaken));
        OnPropertyChanged(nameof(NameWarning));
    }

    /// <summary>
    /// Writes the suggested name, unless the user has already chosen one. Called again whenever the
    /// note changes, so the name follows the note for as long as nobody has touched it.
    /// </summary>
    public void Suggest(string name)
    {
        if (IsNameChanged || name.Length == 0 || Name == name)
        {
            return;
        }

        // Written down before the text moves: the change handler tells a name the dialog chose from
        // one the user typed by comparing the two.
        _suggested = name;
        Name = name;
    }

    /// <summary>Gives the result a free name of its own, next to the one that was taken.</summary>
    public void MakeNameUnique()
    {
        var wanted = Name.Trim();
        if (wanted.Length == 0)
        {
            return;
        }

        var free = VariableNames.Free(wanted, _taken);
        if (free == wanted)
        {
            return;
        }

        IsNameChanged = true;
        _suggested = free;
        Name = free;
    }

    /// <summary>Loads the name an existing step handed this result over under.</summary>
    public void Load(string? name)
    {
        var stored = (name ?? string.Empty).Trim();
        Ticked = true;
        IsNameChanged = true;
        _suggested = stored;
        Name = stored;
    }

    /// <summary>Unticks a result an existing step did not hand over.</summary>
    public void Skip()
    {
        Ticked = false;
        IsNameChanged = false;
        _suggested = string.Empty;
        Name = string.Empty;
    }

    /// <summary>The row this result is written as, or nothing while it is not handed over.</summary>
    public StepParameterRow? ToRow() => Ticked && Name.Trim().Length > 0
        ? new StepParameterRow
        {
            Columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["key"] = Key,
                ["name"] = Name.Trim(),
            },
        }
        : null;

    partial void OnNameChanged(string value)
    {
        if (!string.Equals(value, _suggested, StringComparison.Ordinal))
        {
            IsNameChanged = true;
        }
    }
}
