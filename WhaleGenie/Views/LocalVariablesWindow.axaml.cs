using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

/// <summary>
/// The window that manages the variables a macro makes for itself: what each one holds, where it
/// came from, which steps read it, and what it is called. It cannot add one — a local variable
/// exists because a step writes to it — and it does not delete one either, since the step that
/// writes it is still in the macro. What it can do is rename it, which is the edit that has to
/// happen in one place to stay true everywhere.
/// </summary>
public partial class LocalVariablesWindow : AppWindow
{
    private readonly LocalVariablesViewModel _viewModel;
    private readonly Func<IReadOnlyList<VariableChoice>> _variables;

    public LocalVariablesWindow()
        : this(() => [], (_, _) => (true, string.Empty))
    {
    }

    internal LocalVariablesWindow(Func<IReadOnlyList<VariableChoice>> variables,
        Func<string, string, (bool Ok, string Problem)> rename)
    {
        InitializeComponent();
        WindowChrome.Attach(this);
        Title = Strings.Get("LocalVariables.Title");
        _variables = variables;
        _viewModel = new LocalVariablesViewModel(variables());
        _viewModel.RenameRequested += (old, next) =>
        {
            var (ok, problem) = rename(old, next);
            if (ok)
            {
                // 改名之后整张列表要重取：列表上的名字、来源行里的变量名都跟着变了。
                _viewModel.SetVariables(_variables(), next);
            }
            else
            {
                _viewModel.SayProblem(problem);
            }
        };

        DataContext = _viewModel;
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        Strings.Current.LanguageChanged += RefreshLanguage;
        Closed += (_, _) => Strings.Current.LanguageChanged -= RefreshLanguage;
    }

    internal static async Task ShowAsync(Window owner, Func<IReadOnlyList<VariableChoice>> variables,
        Func<string, string, (bool Ok, string Problem)> rename)
        => await DialogWindow.ShowDialogOver<object?>(
            new LocalVariablesWindow(variables, rename), owner);

    private void RefreshLanguage()
    {
        Title = Strings.Get("LocalVariables.Title");
        _viewModel.RefreshLanguage();
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not Key.Escape)
        {
            return;
        }

        e.Handled = true;
        Close();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
