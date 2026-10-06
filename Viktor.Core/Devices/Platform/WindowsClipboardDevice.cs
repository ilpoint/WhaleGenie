using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Viktor.Core.Devices;

namespace Viktor.Core.Devices.Platform;

/// <summary>
/// The Windows clipboard, read and written through the Win32 calls directly rather than
/// through the interface layer, so a macro can copy and paste while Viktor is in the
/// background and no window has to be in front.
/// </summary>
public sealed class WindowsClipboardDevice : IClipboardDevice
{
    private const uint UnicodeText = 13;
    private const uint DeviceIndependentBitmap = 8;
    private const uint DeviceIndependentBitmapV5 = 17;
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
            Put(UnicodeText, System.Text.Encoding.Unicode.GetBytes(text + "\0"));
            return true;
        });
    }

    public ImageFrame? ReadImage()
    {
        Require();
        return InClipboard(() =>
        {
            // The newer header is preferred when both are there: it is the one that can carry a
            // colour profile, and a reader that understands it understands the older one too.
            var format = IsClipboardFormatAvailable(DeviceIndependentBitmapV5)
                ? DeviceIndependentBitmapV5
                : IsClipboardFormatAvailable(DeviceIndependentBitmap)
                    ? DeviceIndependentBitmap
                    : 0u;

            return format == 0 ? null : ClipboardImage.FromDib(Contents(format));
        });
    }

    public void WriteImage(ImageFrame image)
    {
        Require();
        InClipboard(() =>
        {
            EmptyClipboard();
            Put(DeviceIndependentBitmap, ClipboardImage.ToDib(image));
            return true;
        });
    }

    /// <summary>The bytes of the memory the clipboard is holding for one of its formats.</summary>
    private static byte[] Contents(uint format)
    {
        var handle = GetClipboardData(format);
        if (handle == IntPtr.Zero)
        {
            return [];
        }

        var size = (long)GlobalSize(handle);
        var pointer = GlobalLock(handle);
        if (pointer == IntPtr.Zero || size <= 0)
        {
            if (pointer != IntPtr.Zero)
            {
                GlobalUnlock(handle);
            }

            return [];
        }

        try
        {
            var bytes = new byte[size];
            Marshal.Copy(pointer, bytes, 0, (int)size);
            return bytes;
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    /// <summary>
    /// Hands one block of bytes to the clipboard, which takes over the memory. Every failure along
    /// the way frees what this side allocated, because a block that is neither owned by the
    /// clipboard nor freed here is a leak that stays for the life of the program.
    /// </summary>
    private static void Put(uint format, byte[] bytes)
    {
        var handle = GlobalAlloc(MoveableMemory, (UIntPtr)bytes.Length);
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
            Marshal.Copy(bytes, 0, target, bytes.Length);
        }
        finally
        {
            GlobalUnlock(handle);
        }

        if (SetClipboardData(format, handle) == IntPtr.Zero)
        {
            GlobalFree(handle);
            throw new DeviceActionException("Run.ClipboardWriteFailed");
        }
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
    private static extern UIntPtr GlobalSize(IntPtr memory);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr memory);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr memory);
}
