using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;


namespace WhaleGenie.Views;

// The pills that add a piece: variables, functions, signs and plain values.
public partial class ValuePickerWindow : AppWindow
{
    private Control? ControlAt(Point point)
    {
        for (var walker = Band.InputHitTest(point) as Visual;
             walker is not null;
             walker = walker.GetVisualParent())
        {
            if (walker is Control { DataContext: SlotNode } control)
            {
                return control;
            }
        }

        return null;
    }

    private void OnVariableFilter(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: VariableFilter filter })
        {
            _viewModel.Wall.SelectedFilter = filter;
        }
    }

    private void OnFunctionFilter(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ExpressionFunctionFilter filter })
        {
            _viewModel.SelectedFunctionGroup = filter;
        }
    }

    private void OnOperator(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: ExpressionOperator sign })
        {
            return;
        }

        Put(sign.Insert);
    }

    private void OnValue(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ValueToken value })
        {
            Put(value.Insert);
        }
    }

    /// <summary>
    /// A name goes into the formula, or is handed back at once when the cell holds nothing else:
    /// there is no formula to build in that case, so the name is the answer.
    /// </summary>
    private void OnVariablePill(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: VariableChoice choice })
        {
            return;
        }

        if (_viewModel.NamesOnly)
        {
            _viewModel.Choose(choice.Token);
            return;
        }

        _viewModel.Wall.Looking = choice;
        Put(choice.Token);
    }

    private void OnFunctionPill(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: ExpressionFunctionChoice function })
        {
            return;
        }

        _viewModel.SelectedFunction = function;
        _viewModel.PlaceCall(function.Name);
    }
}
