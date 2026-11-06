using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Localization;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// The overlay that carries the removal cross and the tooltips on the tokens. These are things that
/// only exist once a window has been laid out, so the model tests cannot see them: a cross drawn on
/// an ancestor container, or a tooltip with an empty string, both look fine from the model's side.
/// </summary>
public class ValuePickerWindowTests
{
    private static ValuePickerWindow Window(string formula)
    {
        var form = new ValuePickerViewModel(formula, [], namesOnly: false);
        var window = new ValuePickerWindow(form);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static Control Named(Window window, string name)
        => Assert.IsAssignableFrom<Control>(window.FindControl<Control>(name));

    private static Control[] Pieces(Window window)
        => window.GetVisualDescendants()
            .OfType<Control>()
            .Where(one => one.Classes.Contains("SlotPiece"))
            .ToArray();

    [Fact]
    public void Every_slot_piece_is_a_token_not_a_container()
    {
        Ui.Run(() =>
        {
            var window = Window("concat(list(1, 2), upper($a))");

            var pieces = Pieces(window);
            Assert.NotEmpty(pieces);
            Assert.All(pieces, piece =>
            {
                Assert.IsAssignableFrom<SlotNode>(piece.DataContext);
                Assert.False(piece is ContentControl or ItemsControl or WrapPanel
                    or StackPanel or Grid, "a container inherited the token's DataContext");
            });

            Ui.CloseAndDiscard(window);
        });
    }

    [Fact]
    public void Both_overlay_layers_clip_their_contents()
    {
        Ui.Run(() =>
        {
            var window = Window("$a + 1");

            Assert.True(Named(window, "DragLayer").ClipToBounds);
            Assert.True(Named(window, "RemoveLayer").ClipToBounds);

            Ui.CloseAndDiscard(window);
        });
    }

    [Fact]
    public void A_plain_piece_opens_no_wordless_tooltip()
    {
        Ui.Run(() =>
        {
            var window = Window("$a + 1");

            var pieces = Pieces(window);
            Assert.NotEmpty(pieces);
            Assert.All(pieces, piece => Assert.Null(ToolTip.GetTip(piece)));

            Ui.CloseAndDiscard(window);
        });
    }

    [Fact]
    public void A_problem_still_opens_a_tooltip_with_words()
    {
        Ui.Run(() =>
        {
            var window = Window("nope($a)");

            var piece = Assert.Single(Pieces(window), one => one.DataContext is SlotCall);
            var node = Assert.IsAssignableFrom<SlotNode>(piece.DataContext);
            Assert.True(node.HasProblem);
            Assert.Equal(node.ProblemHint, Assert.IsType<string>(ToolTip.GetTip(piece)));

            Ui.CloseAndDiscard(window);
        });
    }

    [Fact]
    public void The_confirm_button_has_a_tooltip_only_while_it_is_grey()
    {
        Ui.Run(() =>
        {
            var window = Window("upper($a)");
            var form = (ValuePickerViewModel)window.DataContext!;
            var confirm = window.GetVisualDescendants().OfType<Button>()
                .Single(one => Equals(one.Content, Strings.Get("ValuePicker.InsertButton")));

            Assert.Null(ToolTip.GetTip(confirm));
            Assert.Null(form.BlockReasonTip);

            form.Text = "nope($a)";
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(form.BlockReasonTip);
            Assert.NotNull(ToolTip.GetTip(confirm));

            Ui.CloseAndDiscard(window);
        });
    }

    /// <summary>
    /// The cross used to be drawn on the first control that inherited the piece's DataContext,
    /// which for a call was the whole container. Drive the placement directly rather than
    /// synthesising pointer input: a headless pointer gesture leaves a frame behind for the next
    /// test, and the geometry is what this check is about.
    /// </summary>
    [Fact]
    public void The_cross_sits_on_the_hovered_token_not_on_an_ancestor_container()
    {
        Ui.Run(() =>
        {
            var window = Window("concat(list(1, 2), upper($a))");
            var piece = Pieces(window).First(one => one.DataContext is SlotValue value && value.Text == "1");
            var node = Assert.IsAssignableFrom<SlotNode>(piece.DataContext);
            node.IsHovered = true;

            var type = typeof(ValuePickerWindow);
            type.GetField("_hovered", System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance)!.SetValue(window, node);
            type.GetMethod("PlaceRemove", System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance)!.Invoke(window, null);

            var cross = window.FindControl<Button>("HoverRemove")!;
            Assert.True(cross.IsVisible);
            Assert.Same(node, cross.DataContext);

            var layer = Named(window, "RemoveLayer");
            var corner = piece.TranslatePoint(new Point(piece.Bounds.Width, 0), layer);
            Assert.NotNull(corner);
            Assert.InRange(Math.Abs(Canvas.GetLeft(cross) - (corner.Value.X - 16)), 0, 20);
            Assert.InRange(Math.Abs(Canvas.GetTop(cross) - (corner.Value.Y - 14)), 0, 20);
            // The cross sits above the piece: drawn over its corner it would swallow a click meant
            // to put the insertion point after that piece.
            Assert.True(Canvas.GetTop(cross) <= corner.Value.Y);

            node.IsHovered = false;
            Ui.CloseAndDiscard(window);
        });
    }
}
