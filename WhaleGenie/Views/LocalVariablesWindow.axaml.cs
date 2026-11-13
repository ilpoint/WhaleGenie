using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

/// <summary>
/// The one editor for the values a macro keeps for itself. Two kinds of name stand in the list: the
/// ones the macro declares — created, edited and deleted here — and the ones its steps write, which
/// exist because a step wrote them and can only be renamed. Nothing is committed until Save, so
/// Cancel leaves both the steps and the declarations exactly as they were.
/// </summary>
public partial class LocalVariablesWindow : AppWindow
{
    private readonly LocalVariablesViewModel _viewModel;
    private readonly IReadOnlyList<VariableChoice> _expressionVariables;
    private readonly Func<IReadOnlyList<LocalVariableDraft>, string> _apply;
    private readonly TextBox _searchBox;
    private readonly ListBox _variableList;
    private readonly TextBox _nameBox;
    private readonly TextBox _initialBox;
    private bool _allowClose;
    private bool _prompting;

    public LocalVariablesWindow()
        : this([], [], [])
    {
    }

    internal LocalVariablesWindow(IReadOnlyList<MacroLocalVariable> declared,
        IReadOnlyList<VariableChoice> variables,
        IReadOnlyList<VariableChoice> expressionVariables,
        Func<string, string?>? taken = null,
        Func<IReadOnlyList<LocalVariableDraft>, string>? apply = null,
        string? initialName = null)
    {
        InitializeComponent();
        WindowChrome.Attach(this);
        Title = Strings.Get("LocalVariables.Title");
        _expressionVariables = expressionVariables;
        _apply = apply ?? (_ => string.Empty);
        _searchBox = this.FindControl<TextBox>("SearchBox")!;
        _variableList = this.FindControl<ListBox>("VariableList")!;
        _nameBox = this.FindControl<TextBox>("NameBox")!;
        _initialBox = this.FindControl<TextBox>("InitialBox")!;

        _viewModel = new LocalVariablesViewModel(declared, variables, taken);
        _viewModel.Saved += OnSaved;
        _viewModel.Cancelled += RequestClose;
        _viewModel.FocusRequested += FocusProblemField;
        DataContext = _viewModel;
        if (initialName is { Length: > 0 })
        {
            _viewModel.Selected = _viewModel.Shown.FirstOrDefault(row =>
                    string.Equals(row.Name, initialName, StringComparison.OrdinalIgnoreCase))
                ?? _viewModel.Selected;
        }

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        Strings.Current.LanguageChanged += RefreshLanguage;
        Closed += (_, _) => Strings.Current.LanguageChanged -= RefreshLanguage;
    }

    /// <summary>
    /// Opens the manager over a macro. <paramref name="apply"/> commits the rows and answers with
    /// what stopped it, so a name the macro refuses is shown in the window rather than thrown away.
    /// </summary>
    internal static async Task ShowAsync(Window owner, IReadOnlyList<MacroLocalVariable> declared,
        IReadOnlyList<VariableChoice> variables, IReadOnlyList<VariableChoice> expressionVariables,
        Func<string, string?> taken, Func<IReadOnlyList<LocalVariableDraft>, string> apply,
        string? initialName = null)
        => await DialogWindow.ShowDialogOver<object?>(
            new LocalVariablesWindow(declared, variables, expressionVariables, taken, apply,
                initialName), owner);

    private void OnSaved(IReadOnlyList<LocalVariableDraft> rows)
    {
        if (_apply(rows) is { Length: > 0 } problem)
        {
            _viewModel.SayProblem(problem);
            return;
        }

        _allowClose = true;
        Close();
    }

    private async void OnRemove(object? sender, RoutedEventArgs e)
        => await RemoveSelectedAsync();

    private async Task RemoveSelectedAsync()
    {
        if (_viewModel.Selected is not { IsDeclared: true } row)
        {
            return;
        }

        var choice = await ConfirmDialog.ShowAsync(this,
            Strings.Get("LocalVariables.DeleteTitle"),
            Strings.Format("LocalVariables.DeleteMessage", row.DisplayName),
            Strings.Get("LocalVariables.Delete"),
            cancelLabel: Strings.Get("Common.Cancel"));
        if (choice == ConfirmChoice.Primary)
        {
            _viewModel.RemoveCommand.Execute(null);
            Dispatcher.UIThread.Post(() => _variableList.Focus(), DispatcherPriority.Background);
        }
    }

    /// <summary>
    /// Opens the value picker on the starting value. That value is read as one value at the start of
    /// every run, so it uses the same functions and variables as any other whole-value field.
    /// </summary>
    private async void OnOpenInitialExpression(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.Selected is not { IsDeclared: true } row)
        {
            return;
        }

        if (await ValuePickerWindow.ShowFor(this, row.Initial, _expressionVariables,
                ActionValueForm.Value, startOnFormula: true) is { Chosen: true } built)
        {
            row.Initial = FormulaText.Write(built.Value);
        }
    }

    private void FocusProblemField(LocalVariableProblemField field)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_viewModel.Selected is { } row)
            {
                _variableList.ScrollIntoView(row);
            }

            var box = field is LocalVariableProblemField.Initial ? _initialBox : _nameBox;
            box.BringIntoView();
            box.Focus();
            if (ReferenceEquals(box, _nameBox))
            {
                box.SelectAll();
            }
        }, DispatcherPriority.Background);
    }

    private async Task ReportBlockedAsync()
    {
        // The line under the window already says what is wrong; this only puts the caret where the
        // first problem is, so a long declaration list does not have to be searched by eye.
        _viewModel.FocusFirstProblem();
        await Task.CompletedTask;
    }

    private void RequestClose() => Close();

    /// <summary>
    /// Closing, Escape and Cancel take the same route: rows are staged, so leaving without saving
    /// asks once whether to save them, discard them or stay here.
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_allowClose || !_viewModel.HasChanges)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        Dispatcher.UIThread.Post(() => _ = PromptToSaveAsync());
        base.OnClosing(e);
    }

    private async Task PromptToSaveAsync()
    {
        if (_prompting)
        {
            return;
        }

        _prompting = true;
        try
        {
            var choice = await ConfirmDialog.ShowAsync(this,
                Strings.Get("LocalVariables.UnsavedTitle"),
                Strings.Get("LocalVariables.UnsavedMessage"),
                Strings.Get("Common.Save"),
                Strings.Get("LocalVariables.Discard"));
            switch (choice)
            {
                case ConfirmChoice.Primary:
                    _viewModel.SaveCommand.Execute(null);
                    break;
                case ConfirmChoice.Secondary:
                    _allowClose = true;
                    Close();
                    break;
            }
        }
        finally
        {
            _prompting = false;
        }
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not Key.Escape)
        {
            return;
        }

        RequestClose();
        e.Handled = true;
    }

    private void RefreshLanguage()
    {
        Title = Strings.Get("LocalVariables.Title");
        _viewModel.RefreshLanguage();
    }
}
