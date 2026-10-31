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
public partial class ExpressionBuilderWindow : AppWindow
{
    /// <summary>The press that may become a drag, waiting to see whether the pointer moves.</summary>
    private readonly DragStart _drag = new();

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

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

        // What can be dragged is read from the press itself, and a button marks the press as its own
        // on the way past, so the press is taken here whether or not something closer handled it.
        AddHandler(PointerPressedEvent, OnPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnMoved, RoutingStrategies.Bubble, handledEventsToo: true);

        AddHandler(DragDrop.DragEnterEvent, OnDraggedOver);
        AddHandler(DragDrop.DragOverEvent, OnDraggedOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeft);
        AddHandler(DragDrop.DropEvent, OnDropped);
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
            // Where a name goes is decided by where the card is let go, so a click is answered with
            // what to do instead of a name dropped into whatever was picked last.
            Builder.SayDrag();
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
    private void OnWrapWithOperator(object? sender, RoutedEventArgs e) => Builder.SayDrag();

    /// <summary>Fills the picked place with one of the plain values.</summary>
    private void OnPickValue(object? sender, RoutedEventArgs e) => Builder.SayDrag();

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
            Builder.SayDrag();
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

    /// <summary>
    /// Remembers a press that may turn into a drag: which variable, which function, or which piece
    /// of the tree is on its way somewhere. A press that is really a click is left as the click it
    /// is — in a box the pointer is putting the caret in, and on the little buttons a line carries
    /// it is that button being pressed.
    /// </summary>
    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || DragPayload(e.Source) is not { } payload)
        {
            _drag.Forget();
            return;
        }

        _drag.Remember(this, e, payload);
    }

    /// <summary>
    /// What a press would carry if it were dragged somewhere, or nothing when the press is really a
    /// click. A button wears a choice of its own and shows that choice's word inside, so the walk up
    /// from what was pressed is what finds the choice a press belongs to. It is read here, from the
    /// press, rather than off the entry itself, because a button marks the press as its own on the
    /// way past. A check looks at this too: without a platform to drag on, the words a drag would
    /// carry are as far as a session can follow the gesture.
    /// </summary>
    internal string? DragPayload(object? source)
    {
        for (var at = source as Visual; at is not null; at = at.GetVisualParent())
        {
            if (at is not Control control)
            {
                continue;
            }

            switch (control.DataContext)
            {
                case VariableChoice variable:
                    return "var:" + variable.Name;

                case ExpressionFunctionChoice function:
                    return "fn:" + function.Name;

                // An operator and a plain value are dragged the same way: the card carries which one
                // it is, and where it is let go is the piece it wraps or fills.
                case TreeOperator op:
                    return "op:" + op.Token;

                case ValueChoice value:
                    return "val:" + Builder.PayloadOf(value);

                // A press in a box is the pointer putting the caret in it, and on the little buttons
                // a line carries it is that button being pressed. A place with nothing in it has
                // nothing to drag either: the formula is built by dragging what stands for something.
                case ExpressionItemViewModel piece
                    when !Inside<TextBox>(source) && !Inside<Button>(source)
                         && piece.Kind != ExpressionItemKind.Hole && piece.Parent is not null:
                    return Builder.PayloadOf(piece);
            }
        }

        return null;
    }

    private async void OnMoved(object? sender, PointerEventArgs e) => await _drag.Moved(e);

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

    /// <summary>Lights up the line something being dragged would land on, when it can land at all.</summary>
    private void OnDraggedOver(object? sender, DragEventArgs e)
    {
        var row = RowAt(e.Source);
        var payload = ExpressionDrag.Payload(e.DataTransfer);
        if (row is null || payload is null || !Builder.CanLand(row, payload))
        {
            Builder.MarkDrop(null);
            e.DragEffects = DragDropEffects.None;
            return;
        }

        Builder.MarkDrop(row);
        e.DragEffects = DragDropEffects.Copy;
    }

    private void OnDragLeft(object? sender, DragEventArgs e) => Builder.MarkDrop(null);

    private void OnDropped(object? sender, DragEventArgs e)
    {
        var row = RowAt(e.Source);
        var payload = ExpressionDrag.Payload(e.DataTransfer);
        Builder.MarkDrop(null);
        if (row is null || payload is null)
        {
            return;
        }

        Builder.Drop(row, payload);
        e.DragEffects = DragDropEffects.Copy;
    }

    /// <summary>
    /// The line a drag is over. A drag is handed to the line the pointer is on — each line takes
    /// drops of its own — so what the event came from is the line itself, and the walk up from there
    /// is only what tells a line from the space around the lines.
    /// </summary>
    private static ExpressionItemViewModel? RowAt(object? source)
    {
        for (var at = source as Visual; at is not null; at = at.GetVisualParent())
        {
            if (at is Control { DataContext: ExpressionItemViewModel item })
            {
                return item;
            }
        }

        return null;
    }

    /// <summary>
    /// True when what was pressed is a <typeparamref name="T"/>, or something standing inside one:
    /// a press lands on the innermost thing under the pointer, and what that thing belongs to is
    /// what says whether the press was that thing being clicked.
    /// </summary>
    private static bool Inside<T>(object? source)
        where T : Visual
    {
        for (var at = source as Visual; at is not null; at = at.GetVisualParent())
        {
            if (at is T)
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
            && !Inside<TextBox>(e.Source)
            && Builder.Selected is { } picked)
        {
            Builder.Clear(picked);
            e.Handled = true;
        }
    }


}
