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

    private void OnWrapGroup(object? sender, RoutedEventArgs e)
    {
        _viewModel.WrapGroup();
        Band.Focus();
    }

    private void OnWrapList(object? sender, RoutedEventArgs e)
    {
        _viewModel.WrapList();
        Band.Focus();
    }

    /// <summary>
    /// The sign inside a binary or unary opens the list of signs. Choosing one changes the token it
    /// belongs to; the operands stay where they are, which is what a sign inside a token means.
    /// </summary>
    private void OnChangeOperator(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: SlotNode piece } control)
        {
            return;
        }

        var menu = new ContextMenu();
        foreach (var sign in _viewModel.Operators)
        {
            var token = sign.Insert.Trim();
            var allowed = piece is SlotUnary
                ? token is "not" or "-"
                : token is not "not";
            if (!allowed)
            {
                continue;
            }

            var item = new MenuItem { Header = sign.Token };
            item.Click += (_, _) =>
            {
                _viewModel.ChangeOperator(piece, token);
                Band.Focus();
            };
            menu.Items.Add(item);
        }

        menu.Open(control);
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

        if (_viewModel.IsNameMode)
        {
            // A name is one value: selecting it here is what the OK button confirms, so a click
            // that was meant to look at a name does not close the window by accident.
            _viewModel.Wall.Looking = choice;
            return;
        }

        _viewModel.Wall.Looking = choice;
        Put(choice.Name);
    }

    private void OnFunctionPill(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: ExpressionFunctionChoice function })
        {
            return;
        }

        if (!_viewModel.IsValueMode)
        {
            return;
        }

        _viewModel.SelectedFunction = function;
        _viewModel.PlaceCall(function.Name);
    }
}
