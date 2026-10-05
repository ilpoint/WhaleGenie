using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Viktor.Core.Devices.Platform;

/// <summary>
/// The Windows clipboard, read and written through the Win32 calls directly rather than
/// through the interface layer, so a macro can copy and paste while Viktor is in the
/// background and no window has to be in front.
/// </summary>
public sealed class WindowsClipboardDevice : IClipboardDevice
{
    private const uint UnicodeText = 13;
    private const uint MoveableMemory = 0x0002;
    private const int OpenAttempts = 12;
    private const int OpenPauseMs = 15;

    public int ChangeCount
    {
        get
        {
            Require();
            return GetClipboardSequenceNumber();
        }
    }

    public bool HasText
    {
        get
        {
            Require();
            return InClipboard(() => IsClipboardFormatAvailable(UnicodeText));
        }
    }

    public string ReadText()
    {
        Require();
        return InClipboard(() =>
        {
            if (!IsClipboardFormatAvailable(UnicodeText))
            {
                return string.Empty;
            }

            var handle = GetClipboardData(UnicodeText);
            if (handle == IntPtr.Zero)
            {
                return string.Empty;
            }

            var pointer = GlobalLock(handle);
            if (pointer == IntPtr.Zero)
            {
                return string.Empty;
            }

            try
            {
                return Marshal.PtrToStringUni(pointer) ?? string.Empty;
            }
            finally
            {
                GlobalUnlock(handle);
            }
        });
    }

    public void WriteText(string text)
    {
        Require();
        InClipboard(() =>
        {
            EmptyClipboard();

            var characters = (text + "\0").ToCharArray();
            var handle = GlobalAlloc(MoveableMemory, (UIntPtr)(characters.Length * sizeof(char)));
            if (handle == IntPtr.Zero)
            {
                throw new DeviceActionException("Run.ClipboardWriteFailed");
            }

            var target = GlobalLock(handle);
            if (target == IntPtr.Zero)
            {
                GlobalFree(handle);
                throw new DeviceActionException("Run.ClipboardWriteFailed");
            }

            try
            {
                Marshal.Copy(characters, 0, target, characters.Length);
            }
            finally
            {
                GlobalUnlock(handle);
            }

            // Once the clipboard takes the memory it owns it, so it must not be freed here.
            if (SetClipboardData(UnicodeText, handle) == IntPtr.Zero)
            {
                GlobalFree(handle);
                throw new DeviceActionException("Run.ClipboardWriteFailed");
            }

            return true;
        });
    }

    public void Clear()
    {
        Require();
        InClipboard(() =>
        {
            EmptyClipboard();
            return true;
        });
    }

    /// <summary>
    /// Opens the clipboard, waiting a moment when another program is holding it, and closes
    /// it again whatever the work in between does.
    /// </summary>
    private static T InClipboard<T>(Func<T> action)
    {
        if (!Open())
        {
            throw new DeviceActionException("Run.ClipboardBusy");
        }

        try
        {
            return action();
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static bool Open()
    {
        for (var attempt = 0; attempt < OpenAttempts; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                return true;
            }

            Thread.Sleep(OpenPauseMs);
        }

        return false;
    }

    private static void Require()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new DeviceUnavailableException("the clipboard");
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr memory);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll")]
    private static extern int GetClipboardSequenceNumber();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr memory);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr memory);
}
