using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lucide.Avalonia;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Recording;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;
using Key = Avalonia.Input.Key;
using KeyModifiers = Avalonia.Input.KeyModifiers;

namespace WhaleGenie.Tests;

// Keys and the controller: building a chord, the pad on screen, and trying a step on the spot.
public partial class ActionDialogTests
{
    /// <summary>
    /// What the step is saved as carries only the questions the way it was set up asks: a step that
    /// pairs up features has no room for "the third hit", not even the default one.
    /// </summary>
    [Fact]
    public void A_step_saves_only_the_fields_its_way_asks_about()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("vision.findImage");
            viewModel.Parameters.First(parameter => parameter.Definition.Name == "images").Text =
                @"C:\images\ok.png";
            viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "matchIndex").NumberValue = 3;
            Choose(viewModel, "detectAlgorithm", "feature");
            Dispatcher.UIThread.RunJobs();

            MacroStep? saved = null;
            viewModel.CloseRequested += step => saved = step;
            viewModel.SaveCommand.Execute(null);

            Assert.NotNull(saved);
            var names = saved!.Parameters.Select(parameter => parameter.Name).ToList();

            Assert.DoesNotContain("matchIndex", names);
            Assert.DoesNotContain("orderBy", names);
            Assert.Contains("detectAlgorithm", names);
        });
    }

    /// <summary>
    /// One picker answers both "which way" and "comparing what". Two lists let a step say "pair up
    /// the features, comparing pixel for pixel", which is a sentence with nothing behind it — and
    /// whichever half lost was the half doing nothing.
    /// </summary>
    [Fact]
    public void Recognising_a_picture_is_one_choice_of_four()
    {
        Ui.Run(() =>
        {
            foreach (var key in new[] { "vision.findImage", "vision.waitImage", "vision.clickImage" })
            {
                var viewModel = Open(key);
                var folded = viewModel.AdvancedRows.SelectMany(Names).ToList();

                Assert.Contains("detectAlgorithm", folded);
                Assert.DoesNotContain("method", folded);

                var algorithm = viewModel.Parameters
                    .First(parameter => parameter.Definition.Name == "detectAlgorithm");

                Assert.Equal(["normed", "correlated", "difference", "feature"],
                    algorithm.Definition.Options);
                Assert.Equal(4, algorithm.Definition.OptionLabels.Count);
            }
        });
    }

    [Fact]
    public void A_file_step_reads_in_the_order_the_job_is_done_in()
    {
        Ui.Run(() =>
        {
            // Which file, which part of it, where the answer or the data goes — and then the
            // knobs. Filling a step in is answering those questions in that order, so the dialog
            // asks them in that order and keeps the rest behind the fold.
            var reading = Open("file.readCsv");
            Assert.Equal(["path", "hasHeader"], reading.Rows.SelectMany(Names).ToList());

            var folded = reading.AdvancedRows.SelectMany(Names).ToList();
            Assert.Contains("encoding", folded);
            Assert.Contains("separator", folded);
            Assert.Contains("trimCells", folded);
            // Reading part of a file — these columns, these rows — is how the step is tuned rather
            // than what it is, so those fields are folded away with the rest.
            Assert.Contains("columns", folded);
            Assert.Contains("matchColumn", folded);
            Assert.Contains("matchValue", folded);

            // The results the step may hand over are asked for on a page of their own: they are not
            // part of what the step is, they are how the rest of the macro reaches it.
            Assert.Equal(["rows"],
                reading.ReturnsRows.Select(row => row.Key).ToList());

            var writing = Open("file.writeCsv");
            Assert.Equal(["path", "rows", "header", "writeMode"],
                writing.Rows.SelectMany(Names).ToList());
            Assert.Contains("align", writing.AdvancedRows.SelectMany(Names));
            Assert.Empty(writing.ReturnsRows);

            var listing = Open("file.listFiles");
            // 「列什么」紧跟匹配模式：要文件还是文件夹，和挑哪些名字是同一句话。
            Assert.Equal(["folder", "pattern", "kind", "recurse"],
                listing.Rows.SelectMany(Names).ToList());
            Assert.Equal(["files"], listing.ReturnsRows.Select(row => row.Key).ToList());

            var text = Open("file.readText");
            Assert.Equal(["path"], text.Rows.SelectMany(Names).ToList());
            Assert.Equal(["text", "lines"], text.ReturnsRows.Select(row => row.Key).ToList());
        });
    }

    /// <summary>The parameter names on one line of the dialog.</summary>
    private static IEnumerable<string> Names(ParameterRowViewModel row)
    {
        yield return row.First.Definition.Name;
        if (row.Second is { } second)
        {
            yield return second.Definition.Name;
        }
    }

    // -------------------------------------------------------- keys and controller controls

    [Fact]
    public void A_hotkey_is_built_a_key_at_a_time_from_the_keyboard_on_screen()
    {
        Ui.Run(() =>
        {
            var window = new ActionEditorWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.SelectAction("input.hotkey");
            Dispatcher.UIThread.RunJobs();

            // The combination is a keyboard field button. What the keyboard hands back joins what
            // is already there, so Ctrl+Shift+S is put together one key at a time.
            var keys = viewModel.Parameters.First(parameter => parameter.Definition.Name == "keys");
            Assert.True(keys.IsKeys);
            Assert.False(keys.IsKey);

            var field = window.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(button => button.IsEffectivelyVisible
                    && button is not ToggleButton
                    && button.Classes.Contains("Field")
                    && ReferenceEquals(keys, button.DataContext));
            Assert.NotNull(field);
            Assert.Equal(Strings.Get("Add.ChooseKeys"), field.Content);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBox>()
                    .Where(box => box.IsEffectivelyVisible),
                box => ReferenceEquals(keys, box.DataContext));

            // The sides are named apart on the drawn keyboard, so a combination can say which
            // Ctrl or Shift it means — the same names the trigger bindings use.
            keys.AddKey("左Ctrl");
            keys.AddKey("左Shift");
            keys.AddKey("S");
            Assert.Equal("左Ctrl+左Shift+S", keys.CurrentText);

            // A single key field is the one that is replaced, and it stays that way.
            viewModel.SelectAction("input.keyPress");
            Dispatcher.UIThread.RunJobs();
            var single = viewModel.Parameters.First(parameter => parameter.Definition.Name == "key");
            Assert.True(single.IsKey);
            Assert.False(single.IsKeys);
        });
    }

    /// <summary>
    /// A key field has two ways in: the keyboard drawn on screen, and the keyboard under the hands.
    /// Pressing the key is the quick way, and it is what a macro author reaches for when the key is
    /// already in front of them — so a field that only offered the drawing would be a field they
    /// have to hunt on a picture for a key they are looking at.
    /// </summary>
    [Fact]
    public void A_key_field_takes_the_key_pressed_instead_of_clicked()
    {
        Ui.Run(() =>
        {
            var window = new ActionEditorWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.SelectAction("input.keyPress");
            Dispatcher.UIThread.RunJobs();

            var field = viewModel.Parameters.First(parameter => parameter.Definition.Name == "key");
            var capture = CaptureButton(window, field);

            // Clicking it again is how a capture started by mistake is called off, which is what
            // makes Esc free to be the key a field holds.
            capture.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Strings.Get("Add.PressKey"), capture.Content);

            capture.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.False(field.IsCapturingKey);
            Assert.Equal(Strings.Get("Editor.SetKey"), capture.Content);

            // Waiting for a key, then taking the one that arrives: the field holds it and the
            // button goes back to saying what it is for.
            capture.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Press(window, Key.Escape);

            // Esc is a key the field may hold, so it is taken as the key rather than dismissing
            // the form it is being written into.
            Assert.Equal("Esc", field.CurrentText);
            Assert.True(window.IsVisible);
            Assert.False(field.IsCapturingKey);
            Assert.Equal(Strings.Get("Editor.SetKey"), capture.Content);

            capture.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Press(window, Key.F5);

            Assert.Equal("F5", field.CurrentText);
            Assert.False(field.IsCapturingKey);
            Assert.Equal(Strings.Get("Editor.SetKey"), capture.Content);
        });
    }

    /// <summary>
    /// A combination is built one key at a time, the way the keyboard drawn on screen builds it, so
    /// the capture keeps taking keys until it is stopped: Ctrl, then Shift, then S is three keys,
    /// and so is holding all three and pressing S once.
    /// </summary>
    [Fact]
    public void A_combination_field_takes_every_key_pressed_until_it_is_stopped()
    {
        Ui.Run(() =>
        {
            var window = new ActionEditorWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.SelectAction("input.hotkey");
            Dispatcher.UIThread.RunJobs();

            var field = viewModel.Parameters.First(parameter => parameter.Definition.Name == "keys");
            var capture = CaptureButton(window, field);

            capture.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Strings.Get("Add.StopKeyCapture"), capture.Content);

            // Keys pressed one at a time, which is how a combination is put together by hand. A
            // modifier on its own keeps its side, because the two sides are two different keys.
            Press(window, Key.LeftCtrl);
            Release(window, Key.LeftCtrl);
            Assert.Equal("左Ctrl", field.CurrentText);

            Press(window, Key.LeftShift);
            Release(window, Key.LeftShift);
            Assert.Equal("左Ctrl+左Shift", field.CurrentText);

            Press(window, Key.S);
            Assert.Equal("左Ctrl+左Shift+S", field.CurrentText);

            // Keys held together are one press, and it joins what is there. A chord writes its
            // modifiers without a side, the way a recorded combination is written.
            Press(window, Key.LeftCtrl, KeyModifiers.Control);
            Press(window, Key.A, KeyModifiers.Control);
            Assert.Equal("左Ctrl+左Shift+S+Ctrl+A", field.CurrentText);

            // The capture is still up, and Esc is the way out of it rather than a key of its own.
            Assert.True(field.IsCapturingKey);
            Press(window, Key.Escape);
            Assert.Equal("左Ctrl+左Shift+S+Ctrl+A", field.CurrentText);
            Assert.False(field.IsCapturingKey);
            Assert.Equal(Strings.Get("Add.SetCombination"), capture.Content);
        });
    }

    /// <summary>The button on a key field that takes the key pressed on the keyboard.</summary>
    /// <summary>
    /// The button that starts and stops capturing a key. It used to carry a `CaptureToggle` class of
    /// its own; that class never had a style behind it, so it was removed — the lookup now goes by
    /// the library's class plus the parameter the button belongs to, which is what actually picks one
    /// out. **Renaming a class in the markup leaves every lookup of it pointing at nothing.**
    /// </summary>
    private static Button CaptureButton(Window window, StepParameterViewModel parameter)
        => window.GetVisualDescendants().OfType<Button>()
            .First(item => item.IsEffectivelyVisible
                && item.Classes.Contains("Secondary")
                && ReferenceEquals(item.DataContext, parameter));

    /// <summary>Presses one key of the physical keyboard on the window the form stands in.</summary>
    private static void Press(Window window, Key key, KeyModifiers modifiers = KeyModifiers.None)
        => window.RaiseEvent(new Avalonia.Input.KeyEventArgs
        {
            RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
        });

    /// <summary>Lets go of one key, the way a hand does after pressing it.</summary>
    private static void Release(Window window, Key key)
        => window.RaiseEvent(new Avalonia.Input.KeyEventArgs
        {
            RoutedEvent = Avalonia.Input.InputElement.KeyUpEvent,
            Key = key,
        });

    [Fact]
    public void A_key_field_offers_the_virtual_keyboard_beside_it()
    {
        Ui.Run(() =>
        {
            var window = new ActionEditorWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.SelectAction("input.keyPress");
            Dispatcher.UIThread.RunJobs();

            // The key is chosen on the keyboard drawn on screen; there is no text box whose
            // spelling has to be remembered.
            var field = viewModel.Parameters.First(parameter => parameter.Definition.Name == "key");
            Assert.True(field.IsKey);

            var keyboardField = window.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(button => button.IsEffectivelyVisible
                    && button is not ToggleButton
                    && button.Classes.Contains("Field")
                    && ReferenceEquals(field, button.DataContext));
            Assert.NotNull(keyboardField);
            Assert.Equal(Strings.Get("Add.ChooseKey"), keyboardField.Content);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<AutoCompleteBox>()
                    .Where(box => box.IsEffectivelyVisible),
                box => ReferenceEquals(field, box.DataContext));

            field.WriteValue(Fields.Shape("F5"));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("F5", keyboardField.Content);

            keyboardField.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var pad = window.OwnedWindows.OfType<VirtualKeyboardWindow>().Single();
            Assert.Equal(field.Definition.LocalLabel, pad.Filling);
            pad.Close();

            // Nothing else gets it: a plain text field has no keys to point at.
            viewModel.SelectAction("input.typeText");
            Dispatcher.UIThread.RunJobs();

            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>()
                    .Where(button => button.IsEffectivelyVisible),
                button => button is not ToggleButton
                    && button.Classes.Contains("Field")
                    && button.DataContext is StepParameterViewModel { IsKey: true });
        });
    }

    [Fact]
    public void The_controls_of_a_controller_are_named_on_the_pad_drawn_on_screen()
    {
        Ui.Run(() =>
        {
            var window = new ActionEditorWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.SelectAction("gamepad.button");
            Dispatcher.UIThread.RunJobs();

            var button = viewModel.Parameters.First(parameter => parameter.Definition.Name == "button");
            Assert.True(button.IsGamepadPad);
            Assert.False(viewModel.Parameters.First(parameter => parameter.Definition.Name == "buttonMode")
                .IsGamepadPad);

            var pad = window.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(item => item.IsEffectivelyVisible
                    && Equals(item.Content, Strings.Get("Add.OpenGamepad")));
            Assert.NotNull(pad);
            Assert.Same(button, pad.DataContext);

            // What the pad hands back is one of the choices this parameter has, and a control it
            // does not know is left alone rather than written into the step.
            button.Choose("y");
            Assert.Equal("y", Value(viewModel, "button"));
            button.Choose("both");
            Assert.Equal("y", Value(viewModel, "button"));

            // A choice that is not a control of a controller — the button of a mouse click — is
            // picked out of its list and carries no pad.
            viewModel.SelectAction("input.mouseClick");
            Dispatcher.UIThread.RunJobs();

            var mouse = viewModel.Parameters.First(parameter => parameter.Definition.Name == "button");
            Assert.False(mouse.IsGamepadPad);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>()
                    .Where(item => item.IsEffectivelyVisible),
                item => item.DataContext is StepParameterViewModel { IsGamepadPad: true });
        });
    }

    // ---------------------------------------------------------------- trying one field

    /// <summary>
    /// Taking a colour with the magnifier also writes the place it was read from into the step's own
    /// position: what the magnifier was aimed at is the pixel the step is about, so asking where as
    /// well as what is what saves lining the two up by hand afterwards.
    /// </summary>
    [Fact]
    public void Taking_a_colour_writes_the_place_it_was_read_from_into_the_step()
    {
        Ui.RunAsync(async () =>
        {
            var screen = new ColourScreen { Colour = new PixelColor(0x11, 0x22, 0x33) };
            var hook = new ClickingHook();
            var window = new ActionEditorWindow(null, ActionCatalog.Definitions, [], [])
            {
                Devices = new ChecksDevices { Screen = screen, Hook = hook },
            };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.SelectAction("vision.waitColor");
            Dispatcher.UIThread.RunJobs();

            var colour = viewModel.Parameters.First(parameter => parameter.Definition.Name == "color");
            PickColourButton(window, colour).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            // The click that takes the colour is one the magnifier hears through the shared hook,
            // and what it answers with comes back through the window's own queue.
            hook.Click(700, 500);

            var x = viewModel.Parameters.First(parameter => parameter.Definition.Name == "x");
            var y = viewModel.Parameters.First(parameter => parameter.Definition.Name == "y");
            Assert.True(await Settled(() => colour.CurrentText == "#112233"
                    && x.CurrentText == "700" && y.CurrentText == "500"),
                $"the step came back with colour {colour.CurrentText} at {x.CurrentText},{y.CurrentText}");

            window.Close();
            return true;
        });
    }

    /// <summary>The magnifier's own button, which is what opens it for the field it sits on.</summary>
    private static Button PickColourButton(Window window, StepParameterViewModel parameter)
        => window.GetVisualDescendants().OfType<Button>()
            .First(item => item.IsEffectivelyVisible
                && item.Name == "PickColourButton"
                && ReferenceEquals(item.DataContext, parameter));

    /// <summary>
    /// Lets the window's own work catch up: what the magnifier answers with goes through the
    /// dispatcher rather than coming back on the click, so a check that clicked it has to let the
    /// queue run before it reads the fields.
    /// </summary>
    private static async Task<bool> Settled(Func<bool> done)
    {
        for (var turn = 0; turn < 40; turn++)
        {
            Dispatcher.UIThread.RunJobs();
            if (done())
            {
                return true;
            }

            await Task.Delay(25);
        }

        return done();
    }

    /// <summary>
    /// A mouse a check clicks with: the magnifier hears the click through the same hook everything
    /// else uses, and the other six events are there because every hook has them.
    /// </summary>
    private sealed class ClickingHook : IInputHook
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

        /// <summary>A left click at a place, the way the magnifier hears one.</summary>
        public void Click(int x, int y)
            => MouseDown?.Invoke(new MouseHookEvent(HookMouseButton.Left, x, y, false));
    }

    /// <summary>
    /// A screen that answers with one colour wherever it is read, over a desk big enough for the
    /// magnifier to sit on it.
    /// </summary>
    private sealed class ColourScreen : IScreenDevice
    {
        public PixelColor Colour { get; set; }

        public ScreenLayout Desktop { get; } = new(new ScreenArea(0, 0, 1920, 1080),
            [new ScreenDisplay(new ScreenArea(0, 0, 1920, 1080), true)]);

        public ScreenPoint Cursor => new(0, 0);

        public PixelColor PixelAt(int x, int y) => Colour;

        /// <summary>
        /// A frame of this screen's colour: the magnifier reads a picture of the desk rather than
        /// one pixel at a time, so a stand-in whose pixels and pictures disagreed would be a screen
        /// no machine has.
        /// </summary>
        public ImageFrame Capture(int x, int y, int width, int height)
        {
            var pixels = new byte[width * height * 4];
            for (var at = 0; at < pixels.Length; at += 4)
            {
                pixels[at] = Colour.B;
                pixels[at + 1] = Colour.G;
                pixels[at + 2] = Colour.R;
                pixels[at + 3] = 0xFF;
            }

            return new ImageFrame(width, height, pixels);
        }

        public ScreenShot Capture(ScreenCaptureRequest request)
            => NullDeviceLayer.Instance.Screen.Capture(request);
    }

    /// <summary>
    /// 键、组合键与手柄不再各有一颗"测试"按钮：那几颗按钮做的正是"按现在这一步跑一次"，
    /// 和表单下方的运行按钮是同一件事。这里试的是那颗运行按钮：它仍旧把这一步真的发出去。
    /// </summary>
    [Fact]
    public void A_key_field_is_tried_on_the_spot_from_the_run_button()
    {
        Ui.RunAsync(async () =>
        {
            var window = new ActionEditorWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.SelectAction("input.keyPress");
            Dispatcher.UIThread.RunJobs();

            var key = viewModel.Parameters.First(parameter => parameter.Definition.Name == "key");
            key.WriteValue(Fields.Shape("F5"));

            var devices = new RecordingDevices();
            window.Devices = devices;

            RunButton(window).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            // The step is tried off the thread that draws the window, so what it sent arrives a
            // moment after the click.
            Assert.True(await devices.WaitUntil(() => devices.Calls.Count >= 1),
                "the trial never reached the devices");
            Assert.Equal("keyPress F5 50", devices.Calls[0]);

            window.Close();
            return true;
        });
    }

    /// <summary>
    /// 一件放不回原样的事，在按下运行之前要先问一句。原来决定"要不要问"的那段代码读的是一个早已
    /// 改名的字段，所以从表单里关机从来没有问过；现在这句话由目录说了算，试跑与整次运行问的是同一处。
    /// </summary>
    [Fact]
    public void Running_a_step_that_cannot_be_undone_asks_first()
    {
        Ui.RunAsync(async () =>
        {
            var window = new ActionEditorWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.SelectAction("system.power");
            Dispatcher.UIThread.RunJobs();

            viewModel.Parameters.First(parameter => parameter.Definition.Name == "powerAction")
                .Choose("shutDown");
            Dispatcher.UIThread.RunJobs();

            var devices = new RecordingDevices();
            window.Devices = devices;

            RunButton(window).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            await Ui.Until(() => window.OwnedWindows.OfType<ConfirmDialog>().Any(),
                "shutting the machine down from the form did not ask first");

            var prompt = (ConfirmDialogViewModel)Assert
                .Single(window.OwnedWindows.OfType<ConfirmDialog>()).DataContext!;
            Assert.Equal(Strings.Get("Run.CannotBeUndoneTitle"), prompt.Header);
            Assert.True(prompt.IsDanger);
            prompt.CancelCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            await Ui.Until(() => !window.OwnedWindows.Any(), "the question did not close");

            // Saying no is the whole of it: nothing reached the machine.
            Assert.Empty(devices.Calls);

            window.Close();
            Dispatcher.UIThread.RunJobs();
            return true;
        });
    }

    [Fact]
    public void A_controller_control_is_tried_on_the_spot_from_the_run_button()
    {
        Ui.RunAsync(async () =>
        {
            var window = new ActionEditorWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.SelectAction("gamepad.button");
            Dispatcher.UIThread.RunJobs();

            var button = viewModel.Parameters.First(parameter => parameter.Definition.Name == "button");
            button.Choose("y");

            var devices = new RecordingDevices();
            window.Devices = devices;

            RunButton(window).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            // A tap, then the letting go that keeps a tried button from staying down in the game.
            Assert.True(await devices.WaitUntil(() => devices.Calls.Count >= 3),
                $"the trial sent {string.Join(", ", devices.Calls)}");
            Assert.Equal("gamepadButton y True", devices.Calls[0]);
            Assert.Equal("gamepadButton y False", devices.Calls[1]);
            Assert.Equal("gamepadRelease", devices.Calls[^1]);

            window.Close();
            return true;
        });
    }
}
