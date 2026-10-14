using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Execution;
using WhaleGenie.Models;

namespace WhaleGenie.Tests;

/// <summary>
/// A controller button as a binding. Nothing about a controller comes through the hook — Windows has
/// no word for a button going down, only an answer to "what is held now" — so the service reads the
/// controller on a clock of its own and answers the press and the release the way it answers a key.
/// </summary>
public class GamepadTriggerTests
{
    [Fact]
    public void A_controller_button_starts_its_macro_once_per_press()
    {
        Ui.RunAsync(async () =>
        {
            var macro = Macro("Pad A", MacroLoop.Press);
            var pad = new FakeGamepad();
            using var service = Service(pad, macro);
            service.IsEnabled = true;

            pad.Down = GamepadButtons.A;
            service.LookAtGamepad();
            Assert.True(service.IsRunning(macro), "the held controller button did not start its macro");

            // Still held: reading it again and again is one press, not one press per reading. A run
            // that was stopped while the button stays down must therefore stay stopped.
            service.Stop(macro);
            service.LookAtGamepad();
            service.LookAtGamepad();
            Assert.False(service.IsRunning(macro), "holding a button counts as pressing it again");

            // Let go and press again, and it is a press once more.
            pad.Down = GamepadButtons.None;
            service.LookAtGamepad();
            pad.Down = GamepadButtons.A;
            service.LookAtGamepad();
            Assert.True(service.IsRunning(macro), "a second press did not start the macro again");

            service.StopAll();
            return true;
        });
    }

    /// <summary>
    /// The press and the release are the two branches a binding has, and a controller button reaches
    /// them both: one macro waiting for the press runs, the other waiting for the release does not
    /// until the button comes back up.
    /// </summary>
    [Fact]
    public void The_press_and_the_release_are_answered_separately()
    {
        Ui.RunAsync(async () =>
        {
            var onPress = Macro("Pad A", MacroLoop.Press, "按下跑的");
            var onRelease = Macro("Pad A", MacroLoop.Release, "松开跑的");
            var pad = new FakeGamepad();
            using var service = Service(pad, onPress, onRelease);
            service.IsEnabled = true;

            pad.Down = GamepadButtons.A;
            service.LookAtGamepad();
            Assert.True(service.IsRunning(onPress), "the press did not run the macro waiting for it");
            Assert.False(service.IsRunning(onRelease), "the release ran before the button came up");

            service.StopAll();

            pad.Down = GamepadButtons.None;
            service.LookAtGamepad();
            Assert.True(service.IsRunning(onRelease), "the release did not run the macro waiting for it");
            Assert.False(service.IsRunning(onPress), "the release ran the macro waiting for the press");

            service.StopAll();
            return true;
        });
    }

    /// <summary>
    /// Another button is another binding: pressing B is not pressing A, and a binding that is not a
    /// controller button at all — the letter key <c>A</c> — is not one either.
    /// </summary>
    [Fact]
    public void Another_button_or_another_kind_of_binding_is_not_this_one()
    {
        Ui.RunAsync(async () =>
        {
            var letter = Macro("A", MacroLoop.Press, "绑字母键的");
            var pad = new FakeGamepad();
            using var service = Service(pad, letter);
            service.IsEnabled = true;

            pad.Down = GamepadButtons.A;
            service.LookAtGamepad();
            Assert.False(service.IsRunning(letter), "a controller button answered a binding named A");

            pad.Down = GamepadButtons.B;
            service.LookAtGamepad();
            Assert.False(service.IsRunning(letter), "another controller button answered it");

            service.StopAll();
            return true;
        });
    }

    /// <summary>
    /// A controller button can be held down as the front of a combination, the way a modifier key
    /// can: what is held is written down the same way whichever device it came from.
    /// </summary>
    [Fact]
    public void A_controller_button_can_be_held_while_a_key_is_pressed()
    {
        Ui.RunAsync(async () =>
        {
            var macro = Macro("Pad A+F6", MacroLoop.Press, "手柄加键的");
            var pad = new FakeGamepad();
            using var service = Service(pad, macro);
            service.IsEnabled = true;

            service.Answer(Key("F6"), pressed: true);
            Assert.False(service.IsRunning(macro), "the key alone answered a combination");

            pad.Down = GamepadButtons.A;
            service.LookAtGamepad();
            service.Answer(Key("F6"), pressed: true);
            Assert.True(service.IsRunning(macro), "the combination did not start its macro");

            service.StopAll();
            return true;
        });
    }

    /// <summary>
    /// A machine with no controller plugged in reports no buttons held, so a binding of one never
    /// fires — and a button that was down when the controller went away counts as let go, which stops
    /// the macro "while holding" started.
    /// </summary>
    [Fact]
    public void Nothing_plugged_in_fires_nothing_and_lets_go_of_what_was_held()
    {
        Ui.RunAsync(async () =>
        {
            var once = Macro("Pad A", MacroLoop.Press, "按一下的");
            var holding = Macro("Pad B", MacroLoop.Hold, "按住才跑的");
            var pad = new FakeGamepad { Connected = false, Down = GamepadButtons.A | GamepadButtons.B };
            using var service = Service(pad, once, holding);
            service.IsEnabled = true;

            service.LookAtGamepad();
            Assert.False(service.IsRunning(once), "a machine with no controller started a macro");

            // Plugged in, B held: the macro that runs while it is held starts.
            pad.Connected = true;
            service.LookAtGamepad();
            Assert.True(service.IsRunning(holding), "the held button did not start the holding macro");

            // Unplugged while it is still held: as far as the trigger is concerned it was let go.
            pad.Connected = false;
            service.LookAtGamepad();
            Assert.False(service.IsRunning(holding), "a controller going away left the macro running");

            service.StopAll();
            return true;
        });
    }

    /// <summary>
    /// A macro that does one slow thing, so "is it running" has something to see: a run of no steps
    /// is over before it is asked about.
    /// </summary>
    private static MacroItem Macro(string bind, MacroLoop loop, string name = "手柄触发的")
    {
        var macro = new MacroItem
        {
            Name = name,
            TriggerMode = MacroTrigger.KeystrokesButtonInputs,
            BindKey = bind,
            LoopMode = loop,
        };

        macro.Steps.Add(new MacroStep
        {
            Type = "control.delay",
            Parameters =
            [
                new StepParameter
                {
                    Name = "ms",
                    Kind = ActionParameterKind.Number,
                    Value = "800",
                },
            ],
        });

        return macro;
    }

    private static MacroTriggerService Service(FakeGamepad pad, params MacroItem[] macros)
        => new(() => macros, new ChecksDevices { GamepadReading = pad }, new NoWindowEvents());

    private static HookKey Key(string name) => KeyNames.Resolve(name)!.Value;
}
