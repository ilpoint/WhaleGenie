using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;

namespace WhaleGenie.ViewModels;

/// <summary>
/// One line of a call's arguments page: which argument of the called macro this row fills in, and
/// what goes into it. A call and the results it asks for are written the same way, so this is the
/// same kind of line the returns page already has.
/// </summary>
/// <remarks>
/// A row is kept even when the called macro no longer declares the name it holds. Dropping it on the
/// way in would delete a value the author once typed, silently, and the only way back would be to
/// undo a save that looked like it changed something else; keeping it lets the row say out loud that
/// the name is not declared any more.
/// </remarks>
public partial class CallArgumentRowViewModel : ViewModelBase
{
    public CallArgumentRowViewModel(
        string name, string hint, bool required, bool declared, bool given = false,
        string value = "")
    {
        Name = name;
        _hint = hint;
        IsRequired = required;
        IsDeclared = declared;
        Value = value;
        Given = given;
    }

    private string _hint;

    /// <summary>
    /// True while the row is being filled from a file. The two boxes keep each other honest while a
    /// person edits them, but a file may hold both at once — a value and a claim that it was passed
    /// empty — and that has to stay visible: clearing one of them here would decide for the author
    /// and the run would no longer report what the file actually says.
    /// </summary>
    private bool _loading;

    /// <summary>The name the called macro declares this argument under, as the row was written.</summary>
    public string Name { get; }

    public string Hint => _hint;

    public bool HasHint => Hint.Length > 0;

    /// <summary>True when the declaration says every call has to fill this argument in.</summary>
    public bool IsRequired { get; }

    /// <summary>
    /// False for a row naming an argument the called macro no longer declares — after a rename, or
    /// after the argument was removed. The value stays where it is rather than being thrown away,
    /// and the row says so, because a call that quietly loses half its arguments is worse than one
    /// that is marked and still runs.
    /// </summary>
    public bool IsDeclared { get; }

    public bool IsUndeclared => !IsDeclared;

    /// <summary>
    /// What goes into the argument, read the way every other value field is: <c>$n + 1</c> passes a
    /// number and <c>$list</c> passes the list itself. An empty box means this call leaves the
    /// argument out, which is how the declaration's own default applies.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial string Value { get; set; } = string.Empty;

    /// <summary>
    /// True when the box being empty is the value: the call passes an empty value rather than
    /// leaving the argument out. The two are different answers to the same question — an argument
    /// left out takes its default, an argument passed empty is empty — so one has to be said and
    /// the other is what happens when nothing is said.
    /// </summary>
    [ObservableProperty]
    public partial bool Given { get; set; }

    /// <summary>True when this call passes nothing, so whether the argument is filled in shows.</summary>
    public bool IsEmpty => Value.Trim().Length == 0;

    /// <summary>
    /// A box with something in it cannot also say it was passed empty. Clearing one when the other
    /// is set means the two can never be written down together, so the contradiction the engine
    /// refuses never has to be typed in the first place.
    /// </summary>
    partial void OnValueChanged(string value)
    {
        if (!_loading && value.Trim().Length > 0 && Given)
        {
            Given = false;
        }
    }

    partial void OnGivenChanged(bool value)
    {
        if (!_loading && value && Value.Trim().Length > 0)
        {
            Value = string.Empty;
        }
    }

    /// <summary>Fills the row from what a saved step says, both boxes as they were written.</summary>
    public void Load(string value, bool given)
    {
        _loading = true;
        try
        {
            Value = value;
            Given = given;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    /// The row this argument is written as, or nothing when the call leaves it out entirely — an
    /// argument nobody filled in takes its declared default, and writing an empty box down would say
    /// the same thing at greater length. A value that is empty on purpose is written, because that
    /// is the one case where the empty box is the answer.
    /// </summary>
    public StepParameterRow? ToRow()
    {
        var value = Value.Trim();
        if (value.Length == 0 && !Given)
        {
            return null;
        }

        var row = new StepParameterRow
        {
            Columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [FieldNames.Name] = Name,
                [FieldNames.Value] = value,
            },
        };

        if (Given)
        {
            row.Columns[FieldNames.Given] = "true";
        }

        return row;
    }

    /// <summary>
    /// Takes the words under the row: what the argument is, what kind of value it wants and whether
    /// the call has to fill it in. They are built by the form rather than by the row, because they
    /// are read off the declaration and change with the interface language.
    /// </summary>
    public void SetHint(string hint)
    {
        _hint = hint;
        OnPropertyChanged(nameof(Hint));
        OnPropertyChanged(nameof(HasHint));
    }
}
