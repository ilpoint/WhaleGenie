using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Viktor.Models;
using Viktor.ViewModels;
using Viktor.Views;

namespace Viktor.Tests;

/// <summary>
/// The editor's "set key" button, driven with real key events. The release of the key that was
/// just bound has to be swallowed, or a focused button reads Space as a click and arms the
/// capture all over again.
/// </summary>
public class MacroEditorTests
{
    private static (MacroEditorWindow Window, MacroEditorViewModel ViewModel) Open()
    {
        var macro = new MacroItem { Name = "probe" };
        var window = new MacroEditorWindow(macro, [macro]);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, (MacroEditorViewModel)window.DataContext!);
    }

    private static void Tap(MacroEditorWindow window, PhysicalKey key)
    {
        window.KeyPressQwerty(key, RawInputModifiers.None);
        window.KeyReleaseQwerty(key, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public void Binding_the_space_key_does_not_arm_the_capture_again()
    {
        Ui.Run(() =>
        {
            var (window, viewModel) = Open();
            viewModel.SetKeyCommand.Execute(null);

            Tap(window, PhysicalKey.Space);

            Assert.Equal("Space", viewModel.BindKey);
            Assert.False(viewModel.IsCapturingKey);
        });
    }

    [Fact]
    public void Binding_enter_keeps_the_capture_closed()
    {
        Ui.Run(() =>
        {
            var (window, viewModel) = Open();
            viewModel.SetKeyCommand.Execute(null);

            Tap(window, PhysicalKey.Enter);

            Assert.Equal("Enter", viewModel.BindKey);
            Assert.False(viewModel.IsCapturingKey);
        });
    }

    [Theory]
    [InlineData(PhysicalKey.ShiftRight, "右Shift")]
    [InlineData(PhysicalKey.ShiftLeft, "左Shift")]
    [InlineData(PhysicalKey.ControlRight, "右Ctrl")]
    [InlineData(PhysicalKey.NumPad7, "NumPad7")]
    public void A_binding_says_which_key_it_saw(PhysicalKey key, string expected)
    {
        Ui.Run(() =>
        {
            var (window, viewModel) = Open();
            viewModel.SetKeyCommand.Execute(null);

            Tap(window, key);

            Assert.Equal(expected, viewModel.BindKey);
            Assert.Equal(expected, viewModel.BindKeyDisplay);
        });
    }

    [Fact]
    public void The_palette_keeps_one_copy_button()
    {
        Ui.Run(() =>
        {
            var (_, viewModel) = Open();

            // Duplicating and copying were the same idea twice; only Ctrl + C is left.
            Assert.Single(viewModel.ActionPalette, entry => entry.Key == "copy");
            Assert.DoesNotContain(viewModel.ActionPalette, entry => entry.Key == "duplicate");
        });
    }

    [Fact]
    public void Dropping_a_step_further_down_reorders_the_list()
    {
        Ui.Run(() =>
        {
            var (_, viewModel) = Open();
            var first = new MacroStep { Type = "control.delay" };
            var second = new MacroStep { Type = "control.delay" };
            viewModel.AddStep(first);
            viewModel.AddStep(second);

            viewModel.SetSelection([first]);
            viewModel.MoveSelectionTo(2);

            Assert.Equal(2, viewModel.Steps.Count);
            Assert.Same(second, viewModel.Steps[0]);
            Assert.Same(first, viewModel.Steps[1]);

            // The dragged step stays picked, so it can be moved again without another click.
            Assert.Same(first, Assert.Single(viewModel.SelectedSteps));
        });
    }

    [Fact]
    public void Dropping_a_step_where_it_already_was_changes_nothing()
    {
        Ui.Run(() =>
        {
            var (_, viewModel) = Open();
            var first = new MacroStep { Type = "control.delay" };
            var second = new MacroStep { Type = "control.delay" };
            viewModel.AddStep(first);
            viewModel.AddStep(second);

            viewModel.SetSelection([second]);
            viewModel.MoveSelectionTo(1);

            Assert.Same(first, viewModel.Steps[0]);
            Assert.Same(second, viewModel.Steps[1]);

            // A drag that lands where it started must not fill the undo history. One undo
            // therefore reaches past it, to the step that was added before the drag.
            viewModel.UndoCommand.Execute(null);
            Assert.Same(first, Assert.Single(viewModel.Steps));
        });
    }

    [Fact]
    public void A_mouse_drag_carries_a_step_past_the_one_below_it()
    {
        Ui.Run(() =>
        {
            var (window, viewModel) = Open();
            var first = new MacroStep { Type = "control.delay" };
            var second = new MacroStep { Type = "control.delay" };
            viewModel.AddStep(first);
            viewModel.AddStep(second);
            Dispatcher.UIThread.RunJobs();

            var rows = window.GetVisualDescendants().OfType<ListBoxItem>().ToList();
            Assert.Equal(2, rows.Count);

            // Grab the first row by its middle and let go past the middle of the second,
            // which is where a drop "after it" is asked for.
            var from = Waypoint(window, rows[0], 0.5, 0.5);
            var to = Waypoint(window, rows[1], 0.5, 0.9);

            window.MouseDown(from, MouseButton.Left);
            window.MouseMove(to);
            window.MouseUp(to, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Same(second, viewModel.Steps[0]);
            Assert.Same(first, viewModel.Steps[1]);
        });
    }

    /// <summary>A point inside a row, as a fraction of its width and height, in window space.</summary>
    private static Point Waypoint(Window window, Visual row, double x, double y)
        => row.TranslatePoint(new Point(row.Bounds.Width * x, row.Bounds.Height * y), window) ?? default;
}
