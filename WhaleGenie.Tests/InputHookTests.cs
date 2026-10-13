using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Execution;
using WhaleGenie.Models;

namespace WhaleGenie.Tests;

/// <summary>
/// The keyboard and the mouse reach this project as the engine's own events and as nothing else.
/// Two things are worth pinning down: no name from the library behind the hook may appear here,
/// and a bound key has to be answered from those events — including the rule that a key WhaleGenie
/// sent itself is its own output rather than the user asking for something.
/// </summary>
public class InputHookTests
{
    [Fact]
    public void The_interface_never_names_the_library_the_hook_comes_from()
    {
        // The interface asks the engine for the keyboard and the mouse, so which library reads them
        // is the engine's business. A name from that library reaching this project is what the two
        // were entangled by before: the interface would have to be changed whenever it changed.
        var offenders = Directory
            .EnumerateFiles(Path.Combine(Repository(), "WhaleGenie"), "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                           || path.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase))
            .Where(Said)
            .Select(path => Path.GetRelativePath(Repository(), path))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void A_bound_key_is_answered_and_a_key_this_program_sent_is_not()
    {
        Ui.RunAsync(async () =>
        {
            var macro = new MacroItem
            {
                Name = "按键触发的",
                TriggerMode = MacroTrigger.KeystrokesButtonInputs,
                BindKey = "F6",
            };

            // Long enough that the run is still going when it is asked about: starting happens on a
            // thread of its own.
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

            var hook = new FakeInputHook();
            using var service = new MacroTriggerService(
                () => [macro], NullDeviceLayer.Instance, new NoWindowEvents(), hook);

            service.IsEnabled = true;

            // The hook handed in is where every key comes from, so a service that is switched on has
            // to be listening to it.
            Assert.True(hook.Watched, "the service did not listen to the hook it was handed");

            // Pressing the bound key starts the macro. The press is handed in the way the hook's own
            // handler hands one in rather than by raising the event: a dialog window left open by
            // another check holds the triggers back for the rest of the run, and that rule has
            // nothing to do with this one.
            service.Answer(Key("F6"), pressed: true);
            Assert.True(await Started(service, macro), "the bound key did not start its macro");

            service.StopAll();

            // The same key, a moment after this program sent it itself, is its own output: a macro
            // that presses the very key another macro waits for must not set it off.
            WhaleGenieInputGate.Note("F6");
            service.Answer(Key("F6"), pressed: true);
            await Task.Delay(200);
            Assert.False(service.IsRunning(macro));

            // Switching the system off lets the hook go again.
            service.IsEnabled = false;
            Assert.False(hook.Watched);

            return true;
        });
    }

    /// <summary>True when a file under the interface project carries the library's name.</summary>
    private static bool Said(string path)
    {
        // The build's own folders are not the interface's own source.
        var folder = Path.GetDirectoryName(path) ?? string.Empty;
        if (folder.Contains($"{Path.DirectorySeparatorChar}obj", StringComparison.OrdinalIgnoreCase)
            || folder.Contains($"{Path.DirectorySeparatorChar}bin", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return File.ReadAllText(path).Contains("SharpHook", StringComparison.Ordinal);
    }

    private static HookKey Key(string name) => KeyNames.Resolve(name)!.Value;

    /// <summary>Waits for a macro that was started on a thread of its own to be reported as running.</summary>
    private static async Task<bool> Started(MacroTriggerService service, MacroItem macro)
    {
        for (var waited = 0; waited < 2000; waited += 20)
        {
            if (service.IsRunning(macro))
            {
                return true;
            }

            await Task.Delay(20);
        }

        return false;
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

    /// <summary>
    /// A hook with no keyboard behind it, which is what lets the triggers be checked without a
    /// desktop: the keys are handed in by the check rather than by a keyboard.
    /// </summary>
    /// <remarks>
    /// Only the keys are handed out, because that is what the check above drives. The other five
    /// events are there because the interface says every hook has them: they take a subscription and
    /// drop it, and a check that wants one of them turns it into an event of its own.
    /// </remarks>
    private sealed class FakeInputHook : IInputHook
    {
        public event Action<KeyHookEvent>? KeyDown;

        public event Action<KeyHookEvent>? KeyUp;

        public event Action<MouseHookEvent>? MouseDown
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

        /// <summary>True while something is listening for keys, which is what being switched on does.</summary>
        public bool Watched => KeyDown is not null || KeyUp is not null;
    }
}
