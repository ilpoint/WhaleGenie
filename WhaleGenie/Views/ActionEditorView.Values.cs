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
    internal async void OnOpenExpressionBuilder(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: StepParameterViewModel parameter })
        {
            return;
        }

        if (await ValuePickerWindow.ShowFor(HostWindow!, parameter.CurrentText,
                parameter.Variables, parameter.Definition.ReadMode, parameter.ReadAsFormula,
                startOnFormula: true, target: parameter.Label) is { } built)
        {
            parameter.WriteValue(built);
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

        if (await ValuePickerWindow.ShowFor(HostWindow!, cell.Text, cell.Variables,
                ActionReadMode.WholeValue, bareNames: true, startOnFormula: true) is { } built)
        {
            cell.Write(built);
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
                ActionReadMode.VariableName, lookAtStep: LookAtStep) is not { Length: > 0 } picked)
        {
            return;
        }

        // The chip is inside the row's box, so the nearest box is the one to put the name in; a
        // button laid out beside it still reaches the row through the panel it stands in.
        var caret = (button.FindAncestorOfType<TextBox>(true)
                ?? button.FindAncestorOfType<WrapPanel>(true)?.GetVisualDescendants()
                    .OfType<TextBox>().FirstOrDefault())?.CaretIndex;
        row.InsertVariable(picked, caret ?? row.OrVariable.Length);
    }

    /// <summary>
    /// Opens the variable picker on the field that asked for it. A field that can name a variable
    /// should never need the name typed out of memory, and the name goes in where the caret is: a
    /// field is often a piece of text with a variable beside it rather than only a variable.
    /// </summary>
    internal async void OnPickVariable(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: StepParameterViewModel parameter } button
            || DataContext is not AddActionViewModel viewModel)
        {
            return;
        }

        // The caret belongs to the box the button sits in or beside. The chip lives inside the box's
        // own template now, so the nearest box ancestor is the one being filled in; a button laid
        // out beside the box still finds it under the row.
        // Where the caret is, when the field is still one box. A field edited as pieces has no such
        // box: it remembered where the person was instead, and that is what the name goes beside.
        var caret = (button.FindAncestorOfType<AutoCompleteBox>(true)
                ?? button.FindAncestorOfType<Grid>(true)?.GetVisualDescendants()
                    .OfType<AutoCompleteBox>().FirstOrDefault())?.CaretIndex;

        // A field whose whole value is read opens the one window anywhere in the cell, with the
        // caret where it was; a field that only fills names in opens it on the list of names.
        if (parameter.OffersFormula)
        {
            if (await ValuePickerWindow.ShowFor(HostWindow!, parameter.CurrentText,
                    parameter.Variables, parameter.Definition.ReadMode, parameter.ReadAsFormula,
                    startOnFormula: true, target: parameter.Label,
                    startCaret: caret ?? parameter.CurrentText.Length) is { } written)
            {
                parameter.WriteValue(written);
            }

            return;
        }

        if (await ValuePickerWindow.ShowFor(HostWindow!, null, viewModel.CollectVariables(),
                ActionReadMode.VariableName, target: parameter.Label) is not { Length: > 0 } picked)
        {
            return;
        }

        // A name goes in as a piece of the value rather than as the line it is written as: the
        // field keeps a shape, so nothing about the way a formula spells a name reaches the user.
        parameter.InsertName(picked.TrimStart('$'), caret);
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
        if (await ValuePickerWindow.ShowFor(HostWindow!, draft.DefaultValue,
                variables, ActionReadMode.WholeValue, startOnFormula: true) is { } built)
        {
            draft.DefaultValue = built;
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

        parameter.Text = picked.Colour.ToHex();
        viewModel.ApplyCursorPosition(picked.At.X, picked.At.Y);
    }
}
