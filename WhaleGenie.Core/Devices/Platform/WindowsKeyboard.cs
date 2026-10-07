using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// The keyboard layout and input method of the window with the focus: which language is being typed
/// in, and how to switch it to another one.
///
/// A macro that types a shortcut while a Chinese input method is switched on gets the input method's
/// candidates instead of the shortcut, which is the whole reason this exists.
///
/// What cannot be done from here is switching a Chinese input method's own on/off state in another
/// program: Windows keeps an input context per thread, and asking for the focused window's context
/// from another program comes back empty (measured — the window had an input method window of its
/// own, and ImmGetContext still answered zero). Turning Chinese input off for another program is
/// done by sending it the keys that do it, Ctrl+Space or Shift, which the key actions already cover.
/// </summary>
internal static class WindowsKeyboard
{
    /// <summary>
    /// The layout the window with the focus is using, said the way a person would say it —
    /// "中文(简体) - 微软拼音" rather than a handle nobody can read.
    /// </summary>
    public static string Current() => Describe(Focused());

    /// <summary>Every layout installed on this machine, said the same way and in the same order.</summary>
    public static IReadOnlyList<string> Installed()
    {
        var found = new List<string>();
        foreach (var layout in Layouts())
        {
            found.Add(Describe(layout));
        }

        return found;
    }

    /// <summary>
    /// Switches the focused window to the installed layout whose name matches and says which one
    /// that turned out to be, or null when the machine has no such layout. The window is told rather
    /// than the machine, because that is what happens when a person picks a language: the window
    /// they are typing in changes, and everything else keeps its own.
    /// </summary>
    public static string? SwitchTo(string wanted)
    {
        foreach (var layout in Layouts())
        {
            var name = Describe(layout);
            if (!name.Contains(wanted, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var window = GetForegroundWindow();
            if (window == IntPtr.Zero)
            {
                throw new DeviceActionException("Run.NoForegroundWindow");
            }

            _ = ActivateKeyboardLayout(layout, ForProcess);
            _ = PostMessage(window, InputLanguageChange, IntPtr.Zero, layout);

            // The window switches when it reads the message rather than when it is sent, so the new
            // layout is not there the moment this returns: reading straight away answers with the
            // old one. Measured at 16ms, and waiting for it here is what lets the step after this
            // one trust that it is typing in the language that was asked for.
            var started = Stopwatch.GetTimestamp();
            while (Stopwatch.GetElapsedTime(started).TotalMilliseconds < SettleMs)
            {
                var now = Current();
                if (now == name)
                {
                    return name;
                }

                Thread.Sleep(10);
            }

            throw new DeviceActionException("Run.LayoutNotSwitched", name);
        }

        return null;
    }

    /// <summary>The layout of the thread the focused window belongs to, which is the one being typed in.</summary>
    private static IntPtr Focused()
    {
        var window = GetForegroundWindow();
        var thread = window == IntPtr.Zero ? 0 : GetWindowThreadProcessId(window, out _);
        return GetKeyboardLayout(thread);
    }

    private static List<IntPtr> Layouts()
    {
        var count = GetKeyboardLayoutList(0, null);
        if (count <= 0)
        {
            return [];
        }

        var found = new IntPtr[count];
        var written = GetKeyboardLayoutList(count, found);
        return [.. found[..Math.Max(0, written)]];
    }

    /// <summary>
    /// A layout written out as a person would name it: the language first, then the input method
    /// itself when the layout has one. The language is asked for in the machine's own language, so a
    /// Chinese Windows says 中文(简体) rather than "Chinese (Simplified)".
    /// </summary>
    private static string Describe(IntPtr layout)
    {
        // The low half of a layout handle is the language it types in.
        var language = (int)(layout.ToInt64() & 0xFFFF);
        var name = LanguageName(language);
        var method = MethodName(layout);
        return method.Length == 0 ? name : $"{name} - {method}";
    }

    private static string LanguageName(int language)
    {
        var buffer = new StringBuilder(128);
        var written = GetLocaleInfo(language, LocalizedName, buffer, buffer.Capacity);
        return written > 0 ? buffer.ToString() : $"0x{language:X4}";
    }

    private static string MethodName(IntPtr layout)
    {
        var buffer = new StringBuilder(256);
        var written = ImmGetDescription(layout, buffer, buffer.Capacity);
        return written > 0 ? buffer.ToString() : string.Empty;
    }

    /// <summary>
    /// LOCALE_SLOCALIZEDDISPLAYNAME, the name in the machine's own language: a Chinese Windows says
    /// 英语(美国) rather than "English (United States)". The constants around it are easy to get
    /// wrong — 0x04 is the language's name in its own language, 0x6C is a list of scripts and 0x6E
    /// is the locale's tag, which is what this was first written with, and the layout names came out
    /// as "Hani;Hans;".
    /// </summary>
    private const int LocalizedName = 0x00000002;

    /// <summary>WM_INPUTLANGCHANGEREQUEST, which is how a window is asked to type another language.</summary>
    private const uint InputLanguageChange = 0x0050;

    /// <summary>KLF_SETFORPROCESS, or the change would only be this thread's.</summary>
    private static readonly IntPtr ForProcess = new(0x0100);

    /// <summary>How long a window is given to get round to switching its layout.</summary>
    private const int SettleMs = 500;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetKeyboardLayout(uint thread);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetKeyboardLayoutList(int count, IntPtr[]? layouts);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr ActivateKeyboardLayout(IntPtr layout, IntPtr flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr first, IntPtr second);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetLocaleInfo(int language, int kind, StringBuilder text, int size);

    [DllImport("imm32.dll", CharSet = CharSet.Unicode)]
    private static extern int ImmGetDescription(IntPtr layout, StringBuilder text, int size);

}
