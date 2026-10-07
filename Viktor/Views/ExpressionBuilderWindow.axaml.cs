using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Viktor.ViewModels;

namespace Viktor.Views;

/// <summary>
/// The expression builder. It only reads what the macro knows and hands back the text the user
/// put together; the field that opened it keeps the result, so nothing here writes to a step.
/// </summary>
public partial class ExpressionBuilderWindow : Window
{
    public ExpressionBuilderWindow()
        : this(new ExpressionBuilderViewModel())
    {
    }

    public ExpressionBuilderWindow(ExpressionBuilderViewModel viewModel)
    {
        InitializeComponent();

        DataContext = viewModel;
        viewModel.CloseRequested += Close;

        var closeButton = this.FindControl<Button>("CloseButton");
        if (closeButton is not null)
        {
            closeButton.Click += (_, _) => Close(null);
        }

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Opens the builder over <paramref name="owner"/> with what the field already holds, and
    /// reports the finished expression, or null when it was dismissed.
    /// </summary>
    public static Task<string?> ShowFor(Window owner, string expression,
        IReadOnlyList<string> variables)
        => new ExpressionBuilderWindow(new ExpressionBuilderViewModel(expression, variables))
            .ShowDialogOver<string?>(owner);

    /// <summary>Drops an operator in where the caret is, which is what makes the buttons usable mid-word.</summary>
    private void OnInsertOperator(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ExpressionOperator item })
        {
            InsertAtCursor(item.Insert);
        }
    }

    /// <summary>Drops the chosen variable in, written the way an expression reads it.</summary>
    private void OnInsertVariable(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ExpressionBuilderViewModel { VariableToken.Length: > 0 } viewModel)
        {
            InsertAtCursor(viewModel.VariableToken);
        }
    }

    private void InsertAtCursor(string text)
    {
        if (this.FindControl<TextBox>("ExpressionBox") is not { } box)
        {
            return;
        }

        var current = box.Text ?? string.Empty;
        var index = System.Math.Clamp(box.CaretIndex, 0, current.Length);
        box.Text = current[..index] + text + current[index..];
        box.CaretIndex = index + text.Length;
        box.Focus();
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape && DataContext is ExpressionBuilderViewModel viewModel)
        {
            viewModel.CancelCommand.Execute(null);
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
