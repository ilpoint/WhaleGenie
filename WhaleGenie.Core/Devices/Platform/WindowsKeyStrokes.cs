using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// Sends one keystroke to whatever window has the focus: the key down, and the key up.
/// </summary>
/// <remarks>
/// The stroke is built here rather than handed to a library, because the flags are the whole
/// question. A keystroke carries a Windows key code <em>and</em> a scan code, and the extended flag
/// is what tells two keys that share a scan code apart — NumLock from the Pause its scan code 0x45
/// is one byte of, Home from the number pad's 7, the arrows from the number pad's 2, 4, 6 and 8.
/// Sent without the flag a NumLock arrives as Pause: the lock still flips, so nothing looks wrong on
/// the Windows side, while every browser reports a key nobody pressed. So the three facts are taken
/// from <see cref="WindowsKeys"/> and written into the event as they are.
///
/// Whether a key can be sent at all is <see cref="WindowsKeys.CanBePressed"/>'s question; this is
/// only how one that can goes out.
/// </remarks>
internal static class WindowsKeyStrokes
{
    private const uint InputKeyboard = 1;

    private const uint KeyEventExtended = 0x0001;

    private const uint KeyEventKeyUp = 0x0002;

    /// <summary>Presses a key, holds it for as long as asked, and lets it go.</summary>
    internal static void Press(ushort virtualKey, string name, int holdMs)
    {
        Down(virtualKey, name);

        if (holdMs > 0)
        {
            Thread.Sleep(holdMs);
        }

        Up(virtualKey, name);
    }

    /// <summary>Presses a key down and leaves it down.</summary>
    internal static void Down(ushort virtualKey, string name) => Send(virtualKey, name, up: false);

    /// <summary>Lets a key that is down back up.</summary>
    internal static void Up(ushort virtualKey, string name) => Send(virtualKey, name, up: true);

    /// <summary>
    /// Sends one input event. The scan code goes beside the key code because that is what a
    /// keyboard sends and what the extended flag is read against; a key with no scan code never
    /// reaches here, since <see cref="WindowsKeys.CanBePressed"/> has already refused it.
    /// </summary>
    private static void Send(ushort virtualKey, string name, bool up)
    {
        var flags = up ? KeyEventKeyUp : 0u;
        if (WindowsKeys.IsExtended(virtualKey))
        {
            flags |= KeyEventExtended;
        }

        var stroke = new Input
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = virtualKey,
                    ScanCode = WindowsKeys.ScanCode(virtualKey),
                    Flags = flags,
                },
            },
        };

        // A zero answer means the system would not take the event at all — another process with a
        // higher integrity level is holding the desk, or the session is going away. It is said out
        // loud rather than swallowed, because the step that asked for the key has not done its job.
        if (SendInput(1, [stroke], Marshal.SizeOf<Input>()) == 0)
        {
            throw new DeviceActionException(RunKeys.KeyNotPressable, name);
        }
    }

    /// <summary>The one shape <c>SendInput</c> takes: what kind of event, and the event.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;

        public InputUnion Data;
    }

    /// <summary>
    /// Every event kind the system has, of which only the keyboard one is sent here. The mouse is
    /// written down only so that the union is as wide as the system expects it to be.
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KeyboardInput Keyboard;

        [FieldOffset(0)]
        public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;

        public ushort ScanCode;

        public uint Flags;

        public uint Time;

        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;

        public int Y;

        public uint Data;

        public uint Flags;

        public uint Time;

        public nuint ExtraInfo;
    }

    [DllImport("user32.dll", EntryPoint = "SendInput")]
    private static extern uint SendInput(uint count, Input[] strokes, int size);
}
