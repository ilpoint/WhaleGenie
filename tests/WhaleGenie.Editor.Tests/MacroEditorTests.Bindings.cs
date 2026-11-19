using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// What a binding records: a controller button, a key with its modifiers, and the ways a window is named.
public partial class MacroEditorTests
{
    /// <summary>
    /// A controller button can be the binding. Nothing tells the editor about one — Windows has no
    /// event for it — so the editor reads the controller while it is waiting, and the first button
    /// it finds going down is the binding, exactly as the first key is.
    /// </summary>
    [Fact]
    public void A_controller_button_can_be_taken_as_the_binding()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };

            // Held before the waiting starts, so what happens to a button that was already down can
            // be seen as well.
            var pad = new FakeGamepad { Down = GamepadButtons.A };
            var window = new MacroEditorWindow(macro, [macro], null,
                new ChecksDevices { GamepadReading = pad });
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (MacroEditorViewModel)window.DataContext!;
            viewModel.SetKeyCommand.Execute(null);
            Assert.True(viewModel.IsCapturingKey, "the editor is not waiting for a binding");

            // A button that was already down when the waiting started is not a press: it has to come
            // up and go down again, the way a key somebody is resting a finger on has to.
            window.LookAtController();
            Assert.True(viewModel.IsCapturingKey, "a button that was already down finished the capture");

            // Nor is a button nobody is touching.
            pad.Down = GamepadButtons.None;
            window.LookAtController();
            Assert.True(viewModel.IsCapturingKey);

            pad.Down = GamepadButtons.LeftBumper;
            window.LookAtController();

            Assert.Equal("Pad LB", viewModel.BindKey);
            Assert.Equal("Pad LB", viewModel.BindKeyDisplay);
            Assert.False(viewModel.IsCapturingKey, "the capture stayed open after the button");

            // And a button pressed after the capture is over binds nothing again.
            pad.Down = GamepadButtons.B;
            window.LookAtController();
            Assert.Equal("Pad LB", viewModel.BindKey);
        });
    }

    /// <summary>
    /// Every way the engine can compare a window has a translated summary, so a captured rule never
    /// puts a resource key on screen.
    /// </summary>
    [Fact]
    public void The_window_ways_are_offered_in_the_order_the_engine_reads_them()
    {
        Ui.Run(() =>
        {
            var (_, viewModel) = Open();

            foreach (var way in WindowHow.Values)
            {
                viewModel.UseWindowTarget(new WindowRule(Title: "Notepad", How: way));
                Assert.DoesNotContain("WindowRule.", viewModel.WindowTargetSummary,
                    StringComparison.Ordinal);
            }
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
    public void A_binding_takes_the_modifiers_that_are_down_when_its_last_key_arrives()
    {
        Ui.Run(() =>
        {
            var (window, viewModel) = Open();
            viewModel.SetKeyCommand.Execute(null);

            // Ctrl and Shift go down and stay down: pressing a modifier does not finish the capture,
            // because it may be the front of a combination.
            window.KeyPressQwerty(PhysicalKey.ControlLeft, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.ShiftLeft, RawInputModifiers.Control);
            Dispatcher.UIThread.RunJobs();
            Assert.True(viewModel.IsCapturingKey, "a modifier on its own finished the capture");

            window.KeyPressQwerty(PhysicalKey.S, RawInputModifiers.Control | RawInputModifiers.Shift);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Ctrl+Shift+S", viewModel.BindKey);
            Assert.Equal("Ctrl+Shift+S", viewModel.BindKeyDisplay);
            Assert.False(viewModel.IsCapturingKey);

            // Letting the keys go afterwards binds nothing again: the capture was already over.
            window.KeyReleaseQwerty(PhysicalKey.S, RawInputModifiers.Control | RawInputModifiers.Shift);
            window.KeyReleaseQwerty(PhysicalKey.ShiftLeft, RawInputModifiers.Control);
            window.KeyReleaseQwerty(PhysicalKey.ControlLeft, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Ctrl+Shift+S", viewModel.BindKey);
        });
    }

    [Fact]
    public void A_modifier_held_before_the_capture_started_still_counts()
    {
        Ui.Run(() =>
        {
            var (window, viewModel) = Open();
            viewModel.SetKeyCommand.Execute(null);

            // Ctrl went down before the button was pressed, so this window never saw it go down;
            // only the event itself can say that it is held.
            window.KeyPressQwerty(PhysicalKey.S, RawInputModifiers.Control);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Ctrl+S", viewModel.BindKey);
            Assert.False(viewModel.IsCapturingKey);
        });
    }

    [Fact]
    public void A_modifier_that_nothing_follows_is_the_binding_on_its_own()
    {
        Ui.Run(() =>
        {
            var (window, viewModel) = Open();
            viewModel.SetKeyCommand.Execute(null);

            window.KeyPressQwerty(PhysicalKey.ControlLeft, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.True(viewModel.IsCapturingKey);

            // Let go of with nothing pressed in between: that is the binding, kept with the side it
            // was pressed on, the way a trigger bound to Right Shift reads.
            window.KeyReleaseQwerty(PhysicalKey.ControlLeft, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("左Ctrl", viewModel.BindKey);
            Assert.False(viewModel.IsCapturingKey);
        });
    }
}
