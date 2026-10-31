using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using SharpHook.Data;

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

    /// <summary>KEYEVENTF_UNICODE: the event carries a character instead of a key.</summary>
    private const uint KeyEventUnicode = 0x0004;

    /// <summary>WM_CHAR, the message a window takes a typed character from.</summary>
    private const uint CharMessage = 0x0102;

    /// <summary>Presses a key, holds it for as long as asked, and lets it go.</summary>
    internal static void Press(KeyCode key, string name, int holdMs)
    {
        Down(key, name);

        if (holdMs > 0)
        {
            Thread.Sleep(holdMs);
        }

        Up(key, name);
    }

    /// <summary>Presses a key down and leaves it down.</summary>
    internal static void Down(KeyCode key, string name) => Send(key, name, up: false);

    /// <summary>Lets a key that is down back up.</summary>
    internal static void Up(KeyCode key, string name) => Send(key, name, up: true);

    /// <summary>
    /// Types text as text: every character is handed to the window with the focus as the character
    /// it is, never as the key it sits on.
    /// </summary>
    /// <remarks>
    /// Text is not typed by pressing the keys the characters happen to sit on. A key press is
    /// something the system reads: the layout turns it into a character, and an input method turns
    /// it into candidates first. On this machine, with a 双拼 input method switched on, "Hello"
    /// typed as five key presses arrives as 和梁哦 — he, ll and o are three syllables — which is
    /// how this was found.
    ///
    /// A character posted as WM_CHAR skips both: it reaches the window already spelled out. The
    /// first version of this sent characters through SendInput with KEYEVENTF_UNICODE instead and
    /// was still swallowed by the same input method, so the message is the route that works here.
    ///
    /// Line and tab characters are still pressed as keys, because they are not text the window
    /// inserts but commands it acts on, and no input method has an opinion about them.
    /// </remarks>
    internal static void TypeText(string text, int intervalMs)
    {
        if (text.Length == 0)
        {
            return;
        }

        var window = FocusedControl();
        if (window == IntPtr.Zero)
        {
            // Nothing to post to — a window that draws without ever taking the focus. The input
            // queue is all that is left, and it is better than typing nothing.
            SendCharacters(text, intervalMs);
            return;
        }

        var batch = new StringBuilder();

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];

            if (character == '\r')
            {
                // A carriage return written before a line feed is half of one newline, and taking
                // both would put an empty line between every line.
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    continue;
                }

                character = '\n';
            }

            if (character is '\n' or '\t')
            {
                Post(window, batch);
                Press(character == '\n' ? KeyCode.VcEnter : KeyCode.VcTab, character == '\n' ? "Enter" : "Tab", 0);
            }
            else
            {
                batch.Append(character);

                // Typed at a pace the window can follow: each character is posted on its own and
                // the pause comes between them.
                if (intervalMs > 0)
                {
                    Post(window, batch);
                }
            }

            if (intervalMs > 0 && index + 1 < text.Length)
            {
                Thread.Sleep(intervalMs);
            }
        }

        Post(window, batch);
    }

    /// <summary>
    /// Hands a stretch of characters to the window that has the focus, one message each. Answered
    /// by the window rather than by the system, so a refusal — another process with a higher
    /// integrity level holding that window — is seen here instead of passing as typed text.
    /// </summary>
    private static void Post(IntPtr window, StringBuilder batch)
    {
        if (batch.Length == 0)
        {
            return;
        }

        var text = batch.ToString();
        batch.Clear();

        for (var index = 0; index < text.Length; index++)
        {
            // A surrogate pair is two code units and arrives as two messages, the way the keyboard
            // would have sent it; the window puts the pair back together.
            if (!PostMessage(window, CharMessage, new IntPtr(text[index]), new IntPtr(1)))
            {
                throw new DeviceActionException(RunKeys.TextNotTyped);
            }
        }
    }

    /// <summary>
    /// The control inside the window with the focus that would take a typed character: the caret's
    /// window when the application has one, the window itself when it does not.
    /// </summary>
    private static IntPtr FocusedControl()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var thread = GetWindowThreadProcessId(window, out _);
        var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
        if (GetGUIThreadInfo(thread, ref info))
        {
            if (info.Focus != IntPtr.Zero)
            {
                return info.Focus;
            }

            if (info.Active != IntPtr.Zero)
            {
                return info.Active;
            }
        }

        return window;
    }

    /// <summary>
    /// Types text through the input queue with KEYEVENTF_UNICODE, for the case where no window has
    /// the focus to post a character to. An input method that reads the queue can still take these;
    /// the posting route above is the one that does not.
    /// </summary>
    private static void SendCharacters(string text, int intervalMs)
    {
        for (var index = 0; index < text.Length; index++)
        {
            var length = char.IsHighSurrogate(text[index]) && index + 1 < text.Length ? 2 : 1;
            var strokes = new List<Input>(length * 2);

            for (var at = 0; at < length; at++)
            {
                var character = text[index + at];
                strokes.Add(Character(character, up: false));
                strokes.Add(Character(character, up: true));
            }

            if (SendInput((uint)strokes.Count, [.. strokes], Marshal.SizeOf<Input>()) != strokes.Count)
            {
                throw new DeviceActionException(RunKeys.TextNotTyped);
            }

            index += length - 1;

            if (intervalMs > 0 && index + 1 < text.Length)
            {
                Thread.Sleep(intervalMs);
            }
        }
    }

    /// <summary>One character event: a scan code and no key code.</summary>
    private static Input Character(char character, bool up) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion
        {
            Keyboard = new KeyboardInput
            {
                VirtualKey = 0,
                ScanCode = character,
                Flags = KeyEventUnicode | (up ? KeyEventKeyUp : 0u),
            },
        },
    };

    /// <summary>
    /// Sends one input event. The scan code goes beside the key code because that is what a
    /// keyboard sends and what the extended flag is read against; a key with no scan code never
    /// reaches here, since <see cref="WindowsKeys.CanBePressed"/> has already refused it.
    /// </summary>
    private static void Send(KeyCode key, string name, bool up)
    {
        var virtualKey = WindowsKeys.VirtualKey(key)
            ?? throw new DeviceActionException(RunKeys.KeyNotPressable, name);

        var flags = up ? KeyEventKeyUp : 0u;

        // The flag is asked of the key the macro named rather than of its Windows code: the number
        // pad's Enter and the Enter in the middle of the keyboard are one code, and this is what
        // says which of them goes out.
        if (WindowsKeys.IsExtended(key))
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

    /// <summary>
    /// What a thread is up to: which of its windows is in front, and which control inside it holds
    /// the caret. This is how the window that types is found, since a window with the focus is not
    /// the same thing as the control in it that takes characters.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int Size;

        public uint Flags;

        public IntPtr Active;

        public IntPtr Focus;

        public IntPtr Capture;

        public IntPtr MenuOwner;

        public IntPtr MoveSize;

        public IntPtr Caret;

        public int Left;

        public int Top;

        public int Right;

        public int Bottom;
    }

    [DllImport("user32.dll", EntryPoint = "SendInput")]
    private static extern uint SendInput(uint count, Input[] strokes, int size);

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr first, IntPtr second);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
}
