using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

/// <summary>The one editor for project-level globals; the run window only reads their values.</summary>
public partial class GlobalVariablesWindow : Window
{
    private readonly GlobalVariablesViewModel _viewModel;
    private readonly IReadOnlyList<VariableChoice> _expressionVariables;
    private readonly string? _initialName;
    private readonly TextBox _searchBox;
    private readonly ListBox _variableList;
    private readonly TextBox _nameBox;
    private readonly TextBox _defaultValueBox;
    private readonly TextBox _currentValueBox;
    private bool _allowClose;
    private bool _prompting;

    public GlobalVariablesWindow()
        : this([], [], null, null, null)
    {
    }

    internal GlobalVariablesWindow(IEnumerable<VariableDefinition> globals,
        IReadOnlyList<VariableChoice> expressionVariables,
        Func<string, IReadOnlyList<VariableUse>>? usesOf = null,
        Func<string, string?>? renameConflict = null,
        Func<string, IReadOnlyList<string>>? scriptUsers = null,
        string? initialName = null)
    {
        InitializeComponent();
        Title = Strings.Get("GlobalVariables.Title");
        _expressionVariables = expressionVariables;
        _initialName = initialName;
        _searchBox = this.FindControl<TextBox>("SearchBox")!;
        _variableList = this.FindControl<ListBox>("VariableList")!;
        _nameBox = this.FindControl<TextBox>("NameBox")!;
        _defaultValueBox = this.FindControl<TextBox>("DefaultValueBox")!;
        _currentValueBox = this.FindControl<TextBox>("CurrentValueBox")!;

        _viewModel = new GlobalVariablesViewModel(globals, usesOf, renameConflict, scriptUsers);
        _viewModel.Saved += rows =>
        {
            _allowClose = true;
            Close(rows);
        };
        _viewModel.Cancelled += RequestClose;
        _viewModel.FocusRequested += FocusProblemField;
        _viewModel.DefaultExpressionRequested += OnDefaultExpression;
        _viewModel.CurrentExpressionRequested += OnCurrentExpression;
        DataContext = _viewModel;
        if (initialName is { Length: > 0 })
        {
            _viewModel.SelectNamed(initialName);
        }

        if (this.FindControl<Button>("CloseButton") is { } close)
        {
            close.Click += (_, _) => RequestClose();
        }

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        Opened += OnOpened;
        Strings.Current.LanguageChanged += RefreshLanguage;
        Closed += (_, _) => Strings.Current.LanguageChanged -= RefreshLanguage;
    }

    public static async Task<IReadOnlyList<VariableDefinitionDraft>?> EditAsync(
        Window owner, IReadOnlyList<VariableChoice> variables,
        Func<string, IReadOnlyList<VariableUse>>? usesOf = null,
        Func<string, string?>? renameConflict = null,
        Func<string, IReadOnlyList<string>>? scriptUsers = null,
        string? initialName = null)
        => await DialogWindow.ShowDialogOver<IReadOnlyList<VariableDefinitionDraft>?>(
            new GlobalVariablesWindow(VariableCatalog.Globals, variables, usesOf,
                renameConflict, scriptUsers, initialName), owner);

    private void RefreshLanguage()
    {
        Title = Strings.Get("GlobalVariables.Title");
        _viewModel.RefreshLanguage();
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_viewModel.Editor is not null && _initialName is { Length: > 0 })
            {
                _variableList.ScrollIntoView(_viewModel.Editor);
                _nameBox.Focus();
                _nameBox.SelectAll();
            }
            else
            {
                _searchBox.Focus();
            }
        }, DispatcherPriority.Background);
    }

    private async void OnDefaultExpression(VariableDefinitionDraft row)
    {
        if (await ExpressionBuilderWindow.ShowFor(this, row.DefaultValue,
                ExpressionVariablesBefore(row)) is { } built)
        {
            row.DefaultValue = built;
        }
    }

    private async void OnCurrentExpression(VariableDefinitionDraft row)
    {
        if (await ExpressionBuilderWindow.ShowFor(this, row.CurrentValue,
                ExpressionVariablesBefore(row)) is { } built)
        {
            row.CurrentValue = VariableValues.TryRead(row.Type, built, row.Resolver,
                out var value, out _)
                ? value.AsText()
                : built;
        }
    }

    internal IReadOnlyList<VariableChoice> ExpressionVariablesBefore(VariableDefinitionDraft row)
    {
        var choices = new List<VariableChoice>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var choice in _expressionVariables.Where(choice =>
                     choice.Group == VariableGroup.System))
        {
            if (seen.Add(choice.Name))
            {
                choices.Add(choice);
            }
        }

        var stop = _viewModel.Rows.IndexOf(row);
        if (stop < 0)
        {
            return choices;
        }

        for (var index = 0; index < stop; index++)
        {
            var candidate = _viewModel.Rows[index];
            var name = candidate.Name.Trim();
            if (!VariableText.IsName(name) || !seen.Add(name))
            {
                continue;
            }

            choices.Add(new VariableChoice
            {
                Name = name,
                Scope = VariableScope.Global,
                Type = candidate.Type,
                Description = candidate.Description,
                IsPersistent = candidate.PersistValue,
                Origin = VariableOrigin.Global,
                Uses = candidate.Uses,
                ContentReader = () => candidate.CurrentValue,
            });
        }

        return choices;
    }

    private async void OnRemove(object? sender, RoutedEventArgs e)
        => await RemoveSelectedAsync();

    private async Task RemoveSelectedAsync()
    {
        if (_viewModel.Selected is not { } row)
        {
            return;
        }

        var choice = await ConfirmDialog.ShowAsync(this,
            Strings.Get("GlobalVariables.DeleteTitle"),
            Strings.Format("GlobalVariables.DeleteMessage", row.DisplayName),
            Strings.Get("GlobalVariables.Remove"),
            cancelLabel: Strings.Get("Common.Cancel"));
        if (choice == ConfirmChoice.Primary)
        {
            _viewModel.RemoveCommand.Execute(null);
            Dispatcher.UIThread.Post(() => _variableList.Focus(), DispatcherPriority.Background);
        }
    }

    private void FocusProblemField(GlobalVariableProblemField field)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_viewModel.Selected is { } row)
            {
                _variableList.ScrollIntoView(row);
            }

            var box = field switch
            {
                GlobalVariableProblemField.DefaultValue => _defaultValueBox,
                GlobalVariableProblemField.CurrentValue => _currentValueBox,
                _ => _nameBox,
            };
            box.BringIntoView();
            box.Focus();
            if (ReferenceEquals(box, _nameBox))
            {
                box.SelectAll();
            }
        }, DispatcherPriority.Background);
    }

    private void RequestClose() => Close(null);

    /// <summary>
    /// Closing, Escape and Cancel all take the same route: changes are staged, so leaving without
    /// saving asks once whether to save them, discard them or stay here.
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_allowClose || !_viewModel.HasChanges)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;

        // Queued rather than called here. A modal opened from inside a closing handler is asked
        // for before the window has finished saying no, and what comes back depends on how the
        // dialog presents itself — the same call that works for one dialog is answered with
        // silence by another. Queuing it puts the question after the answer that keeps the window
        // open, which is where the person asking is anyway.
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
                Strings.Get("GlobalVariables.UnsavedTitle"),
                Strings.Get("GlobalVariables.UnsavedMessage"),
                Strings.Get("Common.Save"),
                Strings.Get("GlobalVariables.Discard"));
            switch (choice)
            {
                case ConfirmChoice.Primary:
                    _viewModel.SaveCommand.Execute(null);
                    break;
                case ConfirmChoice.Secondary:
                    _allowClose = true;
                    Close(null);
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
        if (e.Key is Key.Escape)
        {
            RequestClose();
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Delete && _variableList.IsKeyboardFocusWithin
            && _viewModel.HasSelection)
        {
            _ = RemoveSelectedAsync();
            e.Handled = true;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            && e.GetPosition(this).Y <= 38)
        {
            BeginMoveDrag(e);
        }
    }
}
