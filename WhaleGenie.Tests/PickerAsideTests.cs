using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using WhaleGenie.Core.Devices;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The pickers that ask the person to point at the screen — a rectangle, a colour, a control. Each
/// one puts every window this program has out of the way first, the window that asked included, and
/// stands on its own while the question is open; the windows come back as they were once it has
/// been answered. What the rectangle was dragged for is usually a picture, and that is taken before
/// the windows come back — a window in front of where the rectangle was drawn would be in it.
/// </summary>
public class PickerAsideTests
{
    [Fact]
    public void A_window_on_the_screen_is_put_away_and_comes_back_the_way_it_was()
    {
        Ui.Run(() =>
        {
            var editor = OnScreen(new Window { WindowState = WindowState.Maximized });
            var asking = OnScreen(new Window());
            var away = new Window();

            using (AppWindows.StepAside([editor, asking, away], asking))
            {
                Assert.False(editor.IsVisible);
                Assert.False(asking.IsVisible);
            }

            // Back as it was: a window that was maximized is still maximized, and one that was
            // never on screen has not been made to appear.
            Assert.True(editor.IsVisible);
            Assert.Equal(WindowState.Maximized, editor.WindowState);
            Assert.True(asking.IsVisible);
            Assert.False(away.IsVisible);

            editor.Close();
            asking.Close();
        });
    }

    [Fact]
    public void A_window_closed_while_it_was_away_is_not_brought_back()
    {
        Ui.Run(() =>
        {
            var editor = OnScreen(new Window());
            var run = OnScreen(new Window());

            using (var aside = AppWindows.StepAside([editor, run]))
            {
                // A run window closes itself when the run ends, which can happen while the picker
                // is up. Showing it again would throw out of a picker that has nothing to do with
                // it, so it is left where it went.
                run.Close();
                Dispatcher.UIThread.RunJobs();
            }

            Assert.True(editor.IsVisible);
            Assert.False(run.IsVisible);

            editor.Close();
        });
    }

    [Fact]
    public void A_window_its_owner_takes_along_comes_back_as_well()
    {
        Ui.Run(() =>
        {
            var editor = OnScreen(new Window());
            var add = new Window();
            add.Show(editor);
            Dispatcher.UIThread.RunJobs();

            // Taking a window off the screen takes the windows it owns with it, so a window put
            // away here is usually off the screen before its own turn comes round. Which windows
            // are on the screen has to be settled before any of them goes: a window that came off
            // with its owner and was read afterwards would never be written down, and nothing
            // would bring it back — the window a step is written in is owned by the editor and
            // goes missing exactly that way.
            editor.PropertyChanged += (_, e) =>
            {
                if (e.Property == Visual.IsVisibleProperty && !editor.IsVisible)
                {
                    add.Hide();
                }
            };

            using (AppWindows.StepAside([editor, add], add))
            {
                Assert.False(editor.IsVisible);
                Assert.False(add.IsVisible);
            }

            Assert.True(editor.IsVisible);
            Assert.True(add.IsVisible, "a window that went away with its owner was not brought back");

            add.Close();
            editor.Close();
        });
    }

    [Fact]
    public void A_picker_shown_on_its_own_is_waited_for_until_it_closes()
    {
        var (visible, waiting, closed) = Ui.RunAsync(async () =>
        {
            var picker = new Window();
            var shown = AppWindows.ShowAndWaitAsync(picker);
            Dispatcher.UIThread.RunJobs();

            var visible = picker.IsVisible;
            var waiting = !shown.IsCompleted;

            picker.Close();
            Dispatcher.UIThread.RunJobs();
            await shown;

            return (visible, waiting, shown.IsCompleted);
        });

        Assert.True(visible, "the picker was not on screen to be waited for");
        Assert.True(waiting, "the wait finished before the picker was closed");
        Assert.True(closed, "the wait outlived the picker it was for");
    }

    [Fact]
    public void The_colour_magnifier_puts_the_program_away_while_it_is_up()
    {
        var (colour, away, back) = Ui.RunAsync(async () =>
        {
            var machine = new ScreenForChecks { Colour = new PixelColor(0x12, 0x34, 0x56) };
            var hook = new HookForChecks();
            var editor = OnScreen(new Window());
            var asking = OnScreen(new Window());

            var desk = AppWindows.Open;
            AppWindows.Open = () => [editor, asking];
            var devices = new ChecksDevices { Screen = machine, Hook = hook };
            Task<ColorPickerWindow.Pick?> picked;
            try
            {
                picked = ColorPickerWindow.PickAsync(asking, machine, devices.Input, hook);
            }
            finally
            {
                AppWindows.Open = desk;
            }

            Dispatcher.UIThread.RunJobs();

            // The window that asked is away as well: the colour being read is behind it as often as
            // behind anything else this program has on the screen.
            var away = !editor.IsVisible && !asking.IsVisible;

            // A click away from the magnifier itself is what takes the colour, and the place it was
            // taken from comes back with it: what the magnifier was aimed at is where the colour is.
            hook.Click(900, 700);
            var colour = await picked;
            Dispatcher.UIThread.RunJobs();

            var back = editor.IsVisible && asking.IsVisible;

            editor.Close();
            asking.Close();
            return (colour, away, back);
        });

        Assert.Equal(new ColorPickerWindow.Pick(new PixelColor(0x12, 0x34, 0x56), new ScreenPoint(900, 700)),
            colour);
        Assert.True(away, "the program did not step aside for the magnifier");
        Assert.True(back, "the program did not come back from the magnifier");
    }

    [Fact]
    public void The_element_picker_puts_the_program_away_while_it_is_up()
    {
        var (pick, away, back) = Ui.RunAsync(async () =>
        {
            var screen = new ScreenForChecks { Cursor = new ScreenPoint(1000, 500) };
            var ui = new UiForChecks { Element = Save };
            var hook = new HookForChecks();
            var editor = OnScreen(new Window());
            var asking = OnScreen(new Window());

            var desk = AppWindows.Open;
            AppWindows.Open = () => [editor, asking];
            Task<ElementPickerWindow.Pick?> picked;
            try
            {
                picked = ElementPickerWindow.PickAsync(asking, screen, hook, ui);
            }
            finally
            {
                AppWindows.Open = desk;
            }

            Dispatcher.UIThread.RunJobs();

            var away = !editor.IsVisible && !asking.IsVisible;

            hook.Click(1000, 500);
            var pick = await picked;
            Dispatcher.UIThread.RunJobs();

            var back = editor.IsVisible && asking.IsVisible;

            editor.Close();
            asking.Close();
            return (pick, away, back);
        });

        Assert.Equal(
            new ElementPickerWindow.Pick("Button[automationId='saveButton']", "Untitled - Notepad"),
            pick);
        Assert.True(away, "the program did not step aside for the element picker");
        Assert.True(back, "the program did not come back from the element picker");
    }

    [Fact]
    public void Dragging_a_rectangle_puts_the_program_away_and_takes_it_back()
    {
        var (region, away, stillAway, back) = Ui.RunAsync(async () =>
        {
            var picker = new RegionPickerWindow(new ScreenForChecks(), new ChecksDevices().Windows);
            var editor = OnScreen(new Window());
            var asking = OnScreen(new Window());

            // The two steps every entry point into this picker takes: the whole program steps
            // aside, and the picker stands on its own until it has an answer.
            var aside = AppWindows.StepAside([editor, asking], asking);
            var taken = picker.TakeAsync();
            Dispatcher.UIThread.RunJobs();

            var away = !editor.IsVisible && !asking.IsVisible;

            picker.MouseDown(new Point(10, 20), MouseButton.Left);
            picker.MouseMove(new Point(40, 60));
            picker.MouseUp(new Point(40, 60), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            picker.KeyPressQwerty(PhysicalKey.F1, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            // The rectangle is known and the windows are still away, which is the moment a picture
            // of it would be taken.
            var stillAway = !editor.IsVisible;
            var region = await taken;

            aside.Dispose();
            var back = editor.IsVisible && asking.IsVisible;

            editor.Close();
            asking.Close();
            return (region, away, stillAway, back);
        });

        Assert.Equal(new RegionPickerWindow.Region(10, 20, 30, 40), region);
        Assert.True(away, "the program did not step aside for the rectangle");
        Assert.True(stillAway, "the windows came back before the picture would have been taken");
        Assert.True(back, "the program did not come back from the rectangle");
    }

    /// <summary>
    /// Every picker of this kind opens the scope and then stands on its own, which is the wiring the
    /// checks above drive by hand: what a picker does with the windows is checked on real windows,
    /// and this is here so a fourth picker cannot quietly ask about the screen with this program's
    /// windows still in front of it.
    /// </summary>
    [Fact]
    public void Every_picker_puts_this_programs_windows_away_first()
    {
        foreach (var name in new[]
                 {
                     "RegionPickerWindow.axaml.cs",
                     "ColorPickerWindow.axaml.cs",
                     "ElementPickerWindow.cs",
                 })
        {
            var source = Source(name);

            Assert.Contains("AppWindows.StepAside(owner)", source, StringComparison.Ordinal);
            Assert.Contains(".TakeAsync();", source, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The dialog a step is written in is waited on for its own answer, not for the modal loop it
    /// is shown in: the pickers above put that window away while they are out, and a hidden modal
    /// window answers at once with nothing while the step is still being written.
    /// </summary>
    [Fact]
    public void The_action_window_is_waited_on_rather_than_its_modal_loop()
    {
        // The action editor is opened from the macro editor, and it carries pickers: its Answer is
        // what is awaited, because a hidden modal window answers nothing.
        var editor = Source("MacroEditorWindow.axaml.cs");
        Assert.Contains("await dialog.Answer", editor, StringComparison.Ordinal);
        Assert.DoesNotContain("await dialog.ShowDialogOver<MacroStep?>", editor,
            StringComparison.Ordinal);

        // The run window no longer opens that editor: a breakpoint's condition is written in a panel
        // of its own, which carries no picker and can be waited on like any small dialog.
        var run = Source("RunWindow.axaml.cs");
        Assert.DoesNotContain("ShowDialogOver<MacroStep?>", run, StringComparison.Ordinal);
        Assert.DoesNotContain("new ActionEditorWindow(", run, StringComparison.Ordinal);
    }

    /// <summary>
    /// The picture of a rectangle is taken inside the scope, once the screen has been drawn again:
    /// the screen answers with the last frame it drew, which still has the picker on it.
    /// </summary>
    [Fact]
    public void The_picture_is_taken_while_the_windows_are_still_away()
    {
        var source = Source("RegionPickerWindow.axaml.cs");

        var away = source.IndexOf("using var aside = AppWindows.StepAside(owner);", StringComparison.Ordinal);
        var waited = source.IndexOf("ScreenComposed.Wait();", StringComparison.Ordinal);
        var picture = source.IndexOf("ImageAssets.Capture(", StringComparison.Ordinal);

        Assert.True(away >= 0 && waited > away && picture > waited,
            "the picture is not taken inside the scope, after the screen has been drawn again");
    }

    /// <summary>The control an element pick is meant to find, which is what the pointer is over.</summary>
    private static readonly UiElementInfo Save = new("Save", "saveButton", "Button", "ButtonClass",
        new ScreenPoint(1000, 500), new ScreenSize(80, 24), "Untitled - Notepad");

    /// <summary>Shows a window, because only a window on screen has anything to step aside from.</summary>
    private static Window OnScreen(Window window)
    {
        window.Show();
        return window;
    }

    /// <summary>The source of one file under WhaleGenie/Views.</summary>
    private static string Source(string name)
        => File.ReadAllText(Path.Combine(Repository(), "WhaleGenie", "Views", name));

    /// <summary>The repository root, found by walking up from the test binaries.</summary>
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

    /// <summary>
    /// A screen that answers with one colour and one pointer position, over a desk these checks can
    /// work on. What it is asked to copy comes back black rather than refused, because both pickers
    /// draw what they read and a picture with nothing in it is still a picture.
    /// </summary>
    private sealed class ScreenForChecks : IScreenDevice
    {
        public PixelColor Colour { get; set; }

        public ScreenPoint Cursor { get; set; }

        public ScreenLayout Desktop { get; } = new(new ScreenArea(0, 0, 1920, 1080),
            [new ScreenDisplay(new ScreenArea(0, 0, 1920, 1080), true)]);

        public PixelColor PixelAt(int x, int y) => Colour;

        public ImageFrame Capture(int x, int y, int width, int height)
            => new(width, height, new byte[width * height * 4]);

        public ScreenShot Capture(ScreenCaptureRequest request)
            => NullDeviceLayer.Instance.Screen.Capture(request);
    }

    /// <summary>
    /// A mouse a check clicks with. The other six events are there because every hook has them, and
    /// only the left button is ever used here.
    /// </summary>
    private sealed class HookForChecks : IInputHook
    {
        public event Action<MouseHookEvent>? MouseDown;

        public event Action<KeyHookEvent>? KeyDown
        {
            add { }
            remove { }
        }

        public event Action<KeyHookEvent>? KeyUp
        {
            add { }
            remove { }
        }

        public event Action<MouseHookEvent>? MouseUp
        {
            add { }
            remove { }
        }

        public event Action<MouseMovedHookEvent>? Moved
        {
            add { }
            remove { }
        }

        public event Action<MouseMovedRelativeHookEvent>? MovedRelative
        {
            add { }
            remove { }
        }

        public event Action<WheelHookEvent>? Wheel
        {
            add { }
            remove { }
        }

        /// <summary>A left click at a place, the way a picker hears one.</summary>
        public void Click(int x, int y)
            => MouseDown?.Invoke(new MouseHookEvent(HookMouseButton.Left, x, y, false));
    }

    /// <summary>
    /// A desktop with one control on it. Everything else is refused the way a machine with nothing
    /// to read refuses it, which is what the element picker never asks.
    /// </summary>
    private sealed class UiForChecks : IUiDevice
    {
        /// <summary>The control the pointer is over, or null when nothing can be named there.</summary>
        public UiElementInfo? Element { get; set; }

        public UiElementInfo? ElementAt(int x, int y) => Element;

        public bool Exists(UiQuery query, int timeoutMs) => Refused<bool>();

        public bool Click(UiQuery query, string button) => Refused<bool>();

        public bool Invoke(UiQuery query) => Refused<bool>();

        public UiElementFacts? Facts(UiQuery query) => Refused<UiElementFacts?>();

        public bool FocusWindow(UiQuery query) => Refused<bool>();

        public bool SetText(UiQuery query, string text, bool clearFirst) => Refused<bool>();

        public IReadOnlyList<UiElementInfo> FindAll(UiQuery query, int limit)
            => Refused<IReadOnlyList<UiElementInfo>>();

        public bool Select(UiQuery query, string text, int itemIndex) => Refused<bool>();

        public bool SetChecked(UiQuery query, bool? state) => Refused<bool>();

        public bool SetExpanded(UiQuery query, string action) => Refused<bool>();

        public bool ScrollIntoView(UiQuery query) => Refused<bool>();

        public UiTable ReadTable(UiQuery query, int limit) => Refused<UiTable>();

        private static T Refused<T>() => throw new DeviceUnavailableException("picking an element");
    }
}
