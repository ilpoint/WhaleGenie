using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

/// <summary>
/// The one window that fills a cell. It lays out what a formula is made of on one page — the signs,
/// the names, the functions — because composing means reaching for the next piece, and a page that
/// puts two of the three away is a page that has to be left and come back to.
/// </summary>
public partial class ValuePickerWindow : AppWindow
{
    private readonly ValuePickerViewModel _viewModel;

    public ValuePickerWindow()
        : this(new ValuePickerViewModel(null, [], namesOnly: false))
    {
    }

    public ValuePickerWindow(ValuePickerViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        // The bar is where a window is dragged by; without this it is a stripe that does nothing.
        WindowChrome.Attach(this);
        Title = viewModel.Title;


        // A cell that only holds a name does not need a workbench: the window is the list.
        if (viewModel.NamesOnly)
        {
            Width = 560;
            Height = 620;
            MinWidth = 480;
            MinHeight = 520;
        }

        viewModel.CloseRequested += result => Close(result);

        Opened += (_, _) =>
        {
            if (!viewModel.CanCompute)
            {
                NameSearch.Focus();
                return;
            }

            var length = FormulaBox.Text?.Length ?? 0;
            FormulaBox.CaretIndex = StartCaret >= 0 ? Math.Clamp(StartCaret, 0, length) : length;
            (StartOnFormula ? FormulaBox : (Control)VariableSearch).Focus();
        };
    }

    /// <summary>True when the window was opened from the formula side of a cell.</summary>
    public bool StartOnFormula { get; init; }

    /// <summary>Where the caret was in the cell, or -1 when nobody said.</summary>
    public int StartCaret { get; init; } = -1;

    /// <summary>
    /// Opens the window on a cell. A cell that only holds a name passes <paramref name="namesOnly"/>:
    /// a formula there is written down as the name itself, so offering one would be a promise the
    /// step cannot keep.
    /// </summary>
    public static async Task<string?> ShowFor(Window owner, string? seed,
        IReadOnlyList<VariableChoice>? variables, bool namesOnly = false, bool startOnFormula = false,
        string? target = null, int startCaret = -1, Action<MacroStep>? lookAtStep = null)
    {
        var viewModel = new ValuePickerViewModel(seed, variables, namesOnly, target);
        if (lookAtStep is not null)
        {
            viewModel.Jumped += lookAtStep;
        }

        var window = new ValuePickerWindow(viewModel)
        {
            StartOnFormula = startOnFormula,
            StartCaret = startCaret,
        };

        return await window.ShowDialog<string?>(owner);
    }

    /// <summary>Where the caret is in the formula, or the end when the box has not been touched.</summary>
    private int Caret => FormulaBox.CaretIndex < 0 ? _viewModel.Text.Length : FormulaBox.CaretIndex;

    /// <summary>
    /// Puts a piece in at the caret and leaves the window open: a formula is built a piece at a
    /// time, and closing on the first piece would make composing a formula a series of openings.
    /// </summary>
    private void Put(string token, int back = 0)
    {
        // A chosen piece of the structure is what the click is about: the new token takes its
        // place. Nothing chosen means the token goes in at the caret as before.
        if (_viewModel.SelectedBlock is { IsHole: false, Text.Length: > 0 } block)
        {
            var at = _viewModel.Text.IndexOf(block.Text, StringComparison.OrdinalIgnoreCase);
            if (at >= 0)
            {
                _viewModel.Text = _viewModel.Text.Remove(at, block.Text.Length).Insert(at, token);
                FormulaBox.CaretIndex = at + token.Length - back;
                FormulaBox.Focus();
                return;
            }
        }

        FormulaBox.CaretIndex = _viewModel.Insert(token, Caret, back);
        FormulaBox.Focus();
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
        if (sender is Control { DataContext: ExpressionOperator sign })
        {
            Put(sign.Insert, ValuePickerViewModel.BackIntoTokens(sign.Insert));
        }
    }

    private void OnValue(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ValueToken value })
        {
            Put(value.Insert, value.Back);
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
        Put(function.Name + "()", back: 1);
    }

    /// <summary>
    /// Escape is the one key this window answers to: it leaves the cell as it was. Every other way
    /// out is a button, so a stray Enter while typing a formula cannot end the work.
    /// </summary>
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _viewModel.Cancel();
            e.Handled = true;
        }
    }

    private void OnInsert(object? sender, RoutedEventArgs e) => _viewModel.Confirm();

    private void OnCancel(object? sender, RoutedEventArgs e) => _viewModel.Cancel();
}
