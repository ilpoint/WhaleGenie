using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
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

    /// <summary>Puts the one cross on a piece the way a pointer resting on it would.</summary>
    private static void Hover(ValuePickerWindow window, Control piece)
    {
        var node = Assert.IsAssignableFrom<SlotNode>(piece.DataContext);
        node.IsHovered = true;
        var type = typeof(ValuePickerWindow);
        type.GetField("_hovered", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(window, node);
        type.GetMethod("PlaceRemove", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(window, null);

        // Layout, then a frame: a control that has just been made visible has no draw list until the
        // renderer produces one, and hit testing is answered from what was drawn.
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// The cross used to be drawn on the first control that inherited the piece's DataContext, which
    /// for a call was the whole container. Drive the placement directly rather than synthesising
    /// pointer input: a headless pointer gesture leaves a frame behind for the next test, and the
    /// geometry is what this check is about.
    /// </summary>
    [Fact]
    public void The_cross_sits_on_the_hovered_token_not_on_an_ancestor_container()
    {
        Ui.Run(() =>
        {
            var window = Window("concat(list(1, 2), upper($a))");
            var piece = Pieces(window).First(one => one.DataContext is SlotValue value && value.Text == "1");
            var node = Assert.IsAssignableFrom<SlotNode>(piece.DataContext);

            Hover(window, piece);

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

    /// <summary>
    /// The cross used to be a rectangle painted into a layer that took no input, so the only part of
    /// it a press could reach was the sliver that happened to hang over the piece. It is a button now:
    /// whatever it draws is what a press lands on, including the part that reaches into the strip's
    /// padding above the first row.
    /// </summary>
    [Fact]
    public void The_cross_is_drawn_where_a_press_lands_on_it()
    {
        Ui.Run(() =>
        {
            var window = Window("concat(list(1, 2), upper($a))");
            var piece = Pieces(window).First(one => one.DataContext is SlotValue value && value.Text == "1");

            Hover(window, piece);

            var cross = window.FindControl<Button>("HoverRemove")!;
            var layer = Named(window, "RemoveLayer");
            var corner = piece.TranslatePoint(new Point(piece.Bounds.Width, 0), layer)!.Value;
            var box = new Rect(Canvas.GetLeft(cross), Canvas.GetTop(cross), cross.Width, cross.Height);

            // Right edge on the piece's own corner, and still above it rather than on it.
            Assert.InRange(corner.X - box.Right, -0.5, 0.5);
            Assert.InRange(corner.Y - box.Bottom, -4, 0.5);

            // Nothing but the layer's own clip may cut it: clamping to the content viewport instead
            // would push a first-row cross back down onto its piece.
            Assert.True(new Rect(layer.Bounds.Size).Contains(box),
                $"the cross at {box} is cut by {layer.Bounds}");

            // The one thing geometry alone cannot say: a press anywhere on it reaches it. Reading it
            // off the layout would pass for a cross drawn outside the layer, or into a region no
            // press ever arrives at — which is exactly how this went wrong before.
            var misses = new List<string>();
            foreach (var at in new[]
                     {
                         new Point(box.X + 8, box.Y + 1),
                         new Point(box.X + 8, box.Y + 8),
                         new Point(box.X + 8, box.Bottom - 1),
                         new Point(box.X + 2, box.Y + 8),
                         new Point(box.Right - 2, box.Y + 8),
                     })
            {
                var hit = window.InputHitTest(layer.TranslatePoint(at, window)!.Value) as Visual;
                if (hit is not null && (ReferenceEquals(hit, cross) || cross.IsVisualAncestorOf(hit)))
                {
                    continue;
                }

                var landed = hit is null
                    ? "nothing"
                    : string.Join(" < ", hit.GetSelfAndVisualAncestors()
                        .Select(one => one.GetType().Name).Take(6));
                misses.Add($"{at} → {landed}");
            }

            Assert.True(misses.Count == 0,
                $"a press inside the cross at {box} reached: {string.Join(" | ", misses)}");

            Ui.CloseAndDiscard(window);
        });
    }

    /// <summary>
    /// The layer the cross lives in has to stay transparent to the pointer, or every piece in the band
    /// under it becomes unclickable. A canvas only takes a press where it draws something.
    /// </summary>
    [Fact]
    public void The_layer_over_the_band_lets_the_pointer_through_but_for_the_cross()
    {
        Ui.Run(() =>
        {
            var window = Window("$a + 1");
            var layer = Assert.IsType<Canvas>(Named(window, "RemoveLayer"));

            Assert.True(layer.IsHitTestVisible);
            Assert.Null(layer.Background);

            var piece = Pieces(window).First();
            var middle = piece.TranslatePoint(
                new Point(piece.Bounds.Width / 2, piece.Bounds.Height / 2), window)!.Value;
            var hit = window.InputHitTest(middle) as Visual;

            Assert.NotNull(hit);
            Assert.False(ReferenceEquals(hit, layer) || layer.IsVisualAncestorOf(hit),
                "the layer took a press that belongs to the band");

            Ui.CloseAndDiscard(window);
        });
    }

    /// <summary>The cross carries the piece it was drawn for, so its click needs no second guess.</summary>
    [Fact]
    public void Clicking_the_cross_takes_that_piece_away()
    {
        Ui.Run(() =>
        {
            var window = Window("concat(list(1, 2), upper($a))");
            var form = (ValuePickerViewModel)window.DataContext!;
            var piece = Pieces(window).First(one => one.DataContext is SlotValue value && value.Text == "2");

            Hover(window, piece);

            var cross = window.FindControl<Button>("HoverRemove")!;
            Assert.Same(piece.DataContext, cross.DataContext);
            Assert.Equal(16, cross.Width);

            cross.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("concat(list(1), upper($a))", form.Text);

            // Nothing from the replaced tree stays armed: that node would cut whatever now stands
            // at the same offsets.
            Assert.False(cross.IsVisible);
            Assert.Null(cross.DataContext);

            Ui.CloseAndDiscard(window);
        });
    }

    /// <summary>
    /// Number and text used to open a row only after a button was pressed. The row is standing now,
    /// so typing never waits for a mode to be chosen and the three panels never jump in height.
    /// </summary>
    [Fact]
    public void The_small_value_field_is_always_visible()
    {
        Ui.Run(() =>
        {
            var window = Window("$a + 1");
            var field = window.FindControl<TextBox>("ValueInput");

            Assert.NotNull(field);
            Assert.True(field.IsEffectivelyVisible);

            Ui.CloseAndDiscard(window);
        });
    }

    /// <summary>Presses one key of the physical keyboard on the window the band stands in.</summary>
    private static void Press(Window window, Key key)
        => window.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
        });

    /// <summary>
    /// The cross keys walk the standing line from one piece edge to the next. The strip takes the
    /// keyboard on a press, so this drives it the way a hand would: focus the strip, then press.
    /// </summary>
    [Fact]
    public void An_arrow_key_moves_the_line_one_place_along()
    {
        Ui.Run(() =>
        {
            var window = Window("$a + $b");
            var form = (ValuePickerViewModel)window.DataContext!;
            var band = Named(window, "Band");
            Assert.True(band.Focusable);
            Assert.True(band.Focus());
            Assert.Equal(form.Text.Length, form.Caret);

            Press(window, Key.Left);
            Assert.Equal(5, form.Caret);

            Press(window, Key.Right);
            Assert.Equal(form.Text.Length, form.Caret);

            Ui.CloseAndDiscard(window);
        });
    }

    /// <summary>
    /// A call too long for one row gives each argument its own row, and the up and down keys are what
    /// carries the line between them. Only the seam it lands on is compared, never a pixel.
    /// </summary>
    [Fact]
    public void The_up_and_down_keys_carry_the_line_between_rows()
    {
        Ui.Run(() =>
        {
            var window = Window("concat($a, $b, $c, $d)");
            var form = (ValuePickerViewModel)window.DataContext!;
            var before = form.Caret;

            Press(window, Key.Up);
            Dispatcher.UIThread.RunJobs();

            Assert.NotEqual(before, form.Caret);
            Assert.Contains(form.Caret, form.Seams());
            Assert.True(window.FindControl<Control>("TextCaret")!.IsVisible);

            Ui.CloseAndDiscard(window);
        });
    }

    /// <summary>A search box moves its own text caret; the band must not take the arrows from it.</summary>
    [Fact]
    public void A_search_box_keeps_its_own_arrow_keys()
    {
        Ui.Run(() =>
        {
            var window = Window("$a + $b");
            var form = (ValuePickerViewModel)window.DataContext!;
            var before = form.Caret;

            var search = Assert.IsType<TextBox>(window.FindControl<TextBox>("VariableSearch"));
            search.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Left,
            });

            Assert.Equal(before, form.Caret);

            Ui.CloseAndDiscard(window);
        });
    }

    /// <summary>The third button beside undo and redo takes the whole formula, and greys out after.</summary>
    [Fact]
    public void The_clear_button_takes_the_whole_formula_in_one_step()
    {
        Ui.Run(() =>
        {
            var window = Window("$a + $b");
            var form = (ValuePickerViewModel)window.DataContext!;
            var clear = window.GetVisualDescendants().OfType<Button>()
                .Single(one => Equals(ToolTip.GetTip(one), Strings.Get("ValuePicker.Clear")));
            Assert.True(clear.IsEnabled);

            clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(string.Empty, form.Text);
            Assert.False(clear.IsEnabled);

            Ui.CloseAndDiscard(window);
        });
    }

    /// <summary>One chip in each panel is the group the list below it belongs to.</summary>
    [Fact]
    public void One_chip_in_each_panel_carries_the_mark()
    {
        Ui.Run(() =>
        {
            var window = Window("$a");
            var form = (ValuePickerViewModel)window.DataContext!;

            var groups = window.GetVisualDescendants().OfType<Button>()
                .Where(one => one.DataContext is ExpressionFunctionFilter)
                .ToArray();
            var markedGroup = Assert.Single(groups, one => one.Classes.Contains("on"));
            Assert.Same(form.SelectedFunctionGroup, markedGroup.DataContext);

            var filters = window.GetVisualDescendants().OfType<Button>()
                .Where(one => one.DataContext is VariableFilter)
                .ToArray();
            var markedFilter = Assert.Single(filters, one => one.Classes.Contains("on"));
            Assert.Same(form.Wall.SelectedFilter, markedFilter.DataContext);

            Ui.CloseAndDiscard(window);
        });
    }

    /// <summary>The line is two pixels now, which is the number `ShowCaret` clamps against.</summary>
    [Fact]
    public void The_standing_line_is_two_pixels_wide()
    {
        Ui.Run(() =>
        {
            var window = Window("$a + $b");
            var caret = Assert.IsType<Border>(window.FindControl<Control>("TextCaret"));

            Assert.Equal(2, caret.Width);
            Assert.NotNull(caret.Background);

            Ui.CloseAndDiscard(window);
        });
    }

    /// <summary>
    /// The blink is a style animation, which no running window hands back, so the markup is what gets
    /// read. Four cues rather than `PlaybackDirection="Alternate"`: alternating fades the line out over
    /// half a second, and a caret is meant to be on and then off.
    /// </summary>
    [Fact]
    public void The_caret_blinks_through_a_style_animation()
    {
        var markup = File.ReadAllText(
            Path.Combine(Repository(), "WhaleGenie", "Views", "ValuePickerWindow.axaml"));

        var at = markup.IndexOf("Selector=\"Border.Caret\"", StringComparison.Ordinal);
        Assert.True(at >= 0, "the caret has no style of its own to hang an animation on");

        var block = markup[at..markup.IndexOf("</Style>", at, StringComparison.Ordinal)];
        Assert.Contains("<Style.Animations>", block, StringComparison.Ordinal);
        Assert.Contains("IterationCount=\"INFINITE\"", block, StringComparison.Ordinal);
        Assert.Contains("Cue=\"49%\"", block, StringComparison.Ordinal);
        Assert.Contains("Cue=\"50%\"", block, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Opacity\" Value=\"0\" />", block, StringComparison.Ordinal);
    }

    private static string Repository()
    {
        for (var at = new DirectoryInfo(AppContext.BaseDirectory); at is not null; at = at.Parent)
        {
            if (File.Exists(Path.Combine(at.FullName, "WhaleGenie.slnx")))
            {
                return at.FullName;
            }
        }

        throw new InvalidOperationException("WhaleGenie.slnx was not found above the test binaries.");
    }
}
