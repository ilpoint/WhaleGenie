using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.ViewModels;

// The parts of the window that belong to one reading of a field: a body of text, or one name.
public sealed partial class ValuePickerViewModel : ViewModelBase
{
    /// <summary>True when this field may name a variable that does not exist yet.</summary>
    public bool AllowNewName { get; }

    /// <summary>
    /// The text a Text field holds. It is the whole value there: no band, no tokens, no formula.
    /// A text field is one body of text, so it is edited as one.
    /// </summary>
    [ObservableProperty]
    public partial string TextBody { get; set; } = string.Empty;

    /// <summary>The name typed into the name search, as a candidate for a new declaration.</summary>
    public string NewName => Wall.SearchText.Trim();

    /// <summary>
    /// True while the typed word is a legal name that this macro does not already have and the
    /// field is one that may write a new name. The list stays the first choice; this row is what
    /// lets a step create a variable without spelling it into a box somewhere else.
    /// </summary>
    public bool CanCreateName => IsNameMode
        && AllowNewName
        && NewName.Length > 0
        && VariableName.IsName(NewName)
        && !_knownNames.Contains(NewName);

    public string NewNameLine => Strings.Format("ValuePicker.Name.Create", NewName);

    /// <summary>Hands back the typed name as the one this field will write.</summary>
    public void CreateName()
    {
        if (CanCreateName)
        {
            CloseRequested?.Invoke(new ValuePick(Formula.Name(NewName), true));
        }
    }

    partial void OnTextBodyChanged(string value)
    {
        OnPropertyChanged(nameof(CanConfirm));
        OnPropertyChanged(nameof(CanClear));
    }
}
