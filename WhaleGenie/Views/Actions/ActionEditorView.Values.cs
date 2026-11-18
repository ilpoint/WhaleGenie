using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

// Naming a value: the expression builder, the variable picker and the colour picker.
public partial class ActionEditorView : UserControl
{
    /// <summary>
    /// Opens the value window on the field that asked for it and keeps what it returns. A field laid
    /// out with buttons of its own still reaches the same window as the one with the button inside.
    /// </summary>
    internal async void OnOpenValuePicker(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: StepParameterViewModel parameter })
        {
            return;
        }

        // One door for every field. The field says how its value is read; the window chooses the
        // mode from that, so a path is typed, a note is typed as text, a name is picked from its
        // list, and only a whole value opens the token band.
        if (await ValuePickerWindow.ShowFor(HostWindow!, parameter.CurrentShape,
                parameter.Variables, parameter.Definition.ValueForm,
                target: parameter.Label,
                allowNewName: parameter.AllowsNewName) is { Chosen: true } built)
        {
            parameter.WriteValue(built.Value);
        }
    }

    /// <summary>
    /// Opens the expression builder on one number of a place to look. The cell carries itself, so
    /// the button does not have to be looked up from the row it stands in, and what comes back is
    /// put in the way the cell reads it: a number stays a number, anything else becomes a formula.
    /// </summary>
    internal async void OnOpenCellExpression(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: NumberCellViewModel cell })
        {
            return;
        }

        if (await ValuePickerWindow.ShowFor(HostWindow!, cell.Seed, cell.Variables,
                ActionValueForm.Value) is { Chosen: true } built)
        {
            cell.Write(built.Value);
        }
    }

    /// <summary>
    /// Opens the variable picker on the box of a row of places to look, and drops the name in where
    /// the caret is. The box belongs to the row rather than to a field of its own, so its caret is
    /// read off the row the button stands in. A row is filled in with a name rather than worked out:
    /// what it holds is a rectangle written out, the name of a picture taken earlier, or the name of
    /// a record that says where to look — a formula in it is not read as one, so the picker offers
    /// no expression editor here.
    /// </summary>
    internal async void OnPickRegionVariable(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: RegionRowViewModel row } button
            || DataContext is not AddActionViewModel viewModel)
        {
            return;
        }

        if (await ValuePickerWindow.ShowFor(HostWindow!, null, viewModel.CollectVariables(),
                ActionValueForm.Name, lookAtStep: LookAtStep) is not { Chosen: true } picked)
        {
            return;
        }

        // The chip is inside the row's box, so the nearest box is the one to put the name in; a
        // button laid out beside it still reaches the row through the panel it stands in.
        var caret = (button.FindAncestorOfType<TextBox>(true)
                ?? button.FindAncestorOfType<WrapPanel>(true)?.GetVisualDescendants()
                    .OfType<TextBox>().FirstOrDefault())?.CaretIndex;
        row.InsertVariable(Formula.NameOf(picked.Value), caret ?? row.OrVariable.Length);
    }

    private async void OnBuildGlobalDefaultExpression(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AddActionViewModel { GlobalVariableDraft: { } draft })
        {
            return;
        }

        var variables = ((AddActionViewModel)DataContext).CollectVariables()
            .Where(choice => choice.Group is VariableGroup.Global or VariableGroup.System)
            .ToList();
        if (await ValuePickerWindow.ShowFor(HostWindow!, draft.Default,
                variables, ActionValueForm.Value) is { Chosen: true } built)
        {
            draft.Default = built.Value;
        }
    }

    /// <summary>
    /// Opens the screen magnifier for a colour parameter and keeps what it reads. The place the
    /// colour was read from goes into the step's own position as well: the pixel worth watching is
    /// the one the magnifier was aimed at, so writing both is what saves lining the two up by hand.
    /// </summary>
    internal async void OnPickParameterColor(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: StepParameterViewModel parameter }
            || DataContext is not AddActionViewModel viewModel)
        {
            return;
        }

        if (await ColorPickerWindow.PickAsync(HostWindow!, Devices.Screen, Devices.Input, Devices.Hook) is not { } picked)
        {
            return;
        }

        parameter.TakeColour(picked.Colour);
        viewModel.ApplyCursorPosition(picked.At.X, picked.At.Y);
    }
}
