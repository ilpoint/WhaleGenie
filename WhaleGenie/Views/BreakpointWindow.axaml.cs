using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

/// <summary>
/// The panel a breakpoint's condition is written in. It answers with what to keep, which is either a
/// rule or nothing at all — the second meaning "stop every time", or "never mind".
/// </summary>
public partial class BreakpointWindow : AppWindow
{
    public BreakpointWindow()
        : this(null, [])
    {
    }

    public BreakpointWindow(BreakpointRule? current, IReadOnlyList<VariableChoice> variables)
    {
        InitializeComponent();
        Title = Strings.Get("Run.BreakpointTitle");
        var viewModel = new BreakpointViewModel(current, variables);
        DataContext = viewModel;
        viewModel.Finished += () => Close(viewModel);
    }

    /// <summary>The builder the action form opens for a condition, opened on the same expression.</summary>
    private async void OnOpenExpressionBuilder(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not BreakpointViewModel form)
        {
            return;
        }

        if (await ExpressionBuilderWindow.ShowFor(this, form.Expression, form.Choices) is { } built)
        {
            form.Expression = built;
        }
    }

    /// <summary>Escape leaves the breakpoint as it was; Enter keeps what was written.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (DataContext is not BreakpointViewModel form)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                form.CancelCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Enter:
                form.SaveCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Opens the panel on a step's breakpoint and answers with the panel itself: it says both how it
    /// ended and what to keep, which is what the caller needs to tell "cleared" from "cancelled".
    /// </summary>
    public static Task<BreakpointViewModel?> EditAsync(Window owner, BreakpointRule? current,
        IReadOnlyList<VariableChoice> variables)
        => new BreakpointWindow(current, variables).ShowDialog<BreakpointViewModel?>(owner);
}
