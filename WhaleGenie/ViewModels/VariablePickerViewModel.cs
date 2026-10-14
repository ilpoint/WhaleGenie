using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.ViewModels;

/// <summary>
/// Backs the small picker that stands beside a field: the same wall the expression builder shows,
/// handing back the text of the variable that was picked. A field that can hold a variable should
/// never need its name typed out of memory.
/// </summary>
public partial class VariablePickerViewModel : ViewModelBase
{
    public VariablePickerViewModel(IReadOnlyList<VariableChoice>? variables = null,
        bool offerBuilder = false)
    {
        OfferBuilder = offerBuilder;
        Wall = new VariableWallViewModel(variables);
        Wall.Chosen += token => CloseRequested?.Invoke(new VariablePick(token, false));
        Wall.Jumped += step => Jumped?.Invoke(step);
    }

    /// <summary>Raised with what was picked, or null when the dialog was dismissed.</summary>
    public event Action<VariablePick?>? CloseRequested;

    /// <summary>
    /// Raised with a step somebody asked to be shown. The picker does not close for it: the person
    /// is still filling a field in, and looking at the step is done beside that rather than instead
    /// of it.
    /// </summary>
    public event Action<MacroStep>? Jumped;

    public string Header => Strings.Get("VarPicker.Title");

    public string Help => Strings.Get("VarPicker.Help");

    /// <summary>
    /// The button that asks for the expression editor instead of a name, which is what a field whose
    /// whole value is read at once needs: a value worked out with a function or a calculation is
    /// something this wall of names cannot offer. Kept away from fields that take a name to write
    /// down and from the ones that match a window by text, where a formula would mean something else.
    /// </summary>
    public bool OfferBuilder { get; }

    public string BuilderLabel => Strings.Get("Add.UseExpression");

    public string BuilderHint => Strings.Get("Add.UseExpressionHint");

    public VariableWallViewModel Wall { get; }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(null);

    [RelayCommand]
    private void UseExpression() => CloseRequested?.Invoke(new VariablePick(string.Empty, true));
}

/// <summary>
/// What the picker hands back: the text of a variable to drop into the field, or the request that
/// the expression editor be opened on it instead. The field itself is untouched either way — the
/// dialog that owns the field is the only thing that writes into it.
/// </summary>
public sealed record VariablePick(string Token, bool WantsExpression);
