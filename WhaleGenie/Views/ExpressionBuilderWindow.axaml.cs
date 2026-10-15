using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

/// <summary>
/// The expression builder. It only reads what the macro knows and hands back the text the user
/// put together; the field that opened it keeps the result, so nothing here writes to a step.
///
/// The window is what tells the two pages apart: one click means "put this in" on the structure
/// page and "type this here" on the page written out by hand, and the same palette serves both.
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
        Title = viewModel.Header;
        viewModel.CloseRequested += Close;

        // The wall knows which variable was picked; which page is in front decides what happens to
        // it, and that belongs to the window.
        viewModel.Wall.Chosen += OnVariableChosen;

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
        IReadOnlyList<VariableChoice> variables)
        => new ExpressionBuilderWindow(new ExpressionBuilderViewModel(expression, variables))
            .ShowDialogOver<string?>(owner);

    /// <summary>What the window is showing, which is what every click here is read against.</summary>
    private ExpressionBuilderViewModel Builder => (ExpressionBuilderViewModel)DataContext!;

    private void OnShowTree(object? sender, RoutedEventArgs e) => Builder.ShowTreeCommand.Execute(null);

    private void OnShowText(object? sender, RoutedEventArgs e) => Builder.ShowTextCommand.Execute(null);

    /// <summary>
    /// A variable was picked. On the structure page the name goes into the place that is picked,
    /// and on the page written out it goes in where the caret is, which is what the wall has always
    /// meant.
    /// </summary>
    private void OnVariableChosen(string token)
    {
        if (Builder.IsTreePage)
        {
            Builder.ChooseVariable(token.TrimStart('$'));
            return;
        }

        InsertAtCursor(token);
    }

    /// <summary>Drops an operator in where the caret is, which is what makes the buttons usable mid-word.</summary>
    private void OnInsertOperator(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ExpressionOperator item })
        {
            InsertAtCursor(item.Insert);
        }
    }

    /// <summary>Wraps the place that is picked in an operator, and moves the pick onto where the user writes next.</summary>
    private void OnWrapWithOperator(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: TreeOperator item })
        {
            Builder.ChooseOperator(item);
        }
    }

    /// <summary>Fills the picked place with one of the plain values.</summary>
    private void OnPickValue(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ValueChoice item })
        {
            Builder.ChooseValue(item);
        }
    }

    /// <summary>
    /// A function was picked. On the structure page it holds the place that is picked as its first
    /// argument; on the page written out, its name and its opening bracket are dropped in at the
    /// caret, leaving it where the arguments go.
    /// </summary>
    private void OnInsertFunction(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: ExpressionFunctionChoice item })
        {
            return;
        }

        if (Builder.IsTreePage)
        {
            Builder.ChooseFunction(item);
            return;
        }

        InsertAtCursor(item.Insert);
    }

    /// <summary>Picks the line that was pressed, which is what the next click from the palette goes into.</summary>
    private void OnRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: ExpressionItemViewModel item })
        {
            Builder.Select(item);
        }
    }

    private void OnAddChild(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ExpressionItemViewModel item })
        {
            Builder.AddChild(item);
        }
    }

    private void OnDropChild(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ExpressionItemViewModel item })
        {
            Builder.DropChild(item);
        }
    }

    /// <summary>True when what was pressed is a box, where the pointer is putting the caret in it.</summary>
    private static bool InsideBox(object? source)
    {
        for (var at = source as Visual; at is not null; at = at.GetVisualParent())
        {
            if (at is TextBox)
            {
                return true;
            }
        }

        return false;
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

    /// <summary>
    /// Escape gives up on the dialog, and Delete does to the picked place what it does to a step:
    /// takes it away. A box the pointer is in gets the key itself, where it is an ordinary edit.
    /// </summary>
    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape)
        {
            Builder.CancelCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Delete or Key.Back
            && Builder.IsTreePage
            && !InsideBox(e.Source)
            && Builder.Selected is { } picked)
        {
            Builder.Clear(picked);
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
