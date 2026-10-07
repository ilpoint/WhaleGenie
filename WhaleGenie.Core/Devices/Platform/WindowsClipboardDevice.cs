using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// The Windows clipboard, read and written through the Win32 calls directly rather than
/// through the interface layer, so a macro can copy and paste while WhaleGenie is in the
/// background and no window has to be in front.
/// </summary>
public sealed class WindowsClipboardDevice : IClipboardDevice
{
    private const uint UnicodeText = 13;
    private const uint DeviceIndependentBitmap = 8;
    private const uint DeviceIndependentBitmapV5 = 17;
    private const uint ShellFileList = 15;
    private const uint MoveableMemory = 0x0002;

    /// <summary>How many bytes the shell's file-list header takes up before the paths start.</summary>
    private const int DropHeader = 20;
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

    public IReadOnlyList<string> ReadFiles()
    {
        Require();
        return InClipboard(() =>
        {
            if (!IsClipboardFormatAvailable(ShellFileList))
            {
                return [];
            }

            var bytes = Contents(ShellFileList);
            if (bytes.Length < DropHeader)
            {
                return [];
            }

            // The header says where the list starts and whether the names are Unicode. Explorer,
            // and everything that copies files like Explorer, writes them in Unicode.
            var start = ReadInt(bytes, 0);
            var wide = ReadInt(bytes, 16) != 0;
            if (start < DropHeader || start >= bytes.Length)
            {
                return [];
            }

            return wide ? WidePaths(bytes, start) : NarrowPaths(bytes, start);
        });
    }

    public void WriteFiles(IReadOnlyList<string> paths)
    {
        Require();
        InClipboard(() =>
        {
            EmptyClipboard();
            Put(ShellFileList, DropFiles(paths));
            return true;
        });
    }

    /// <summary>
    /// The block the shell reads a file list out of: a header, then the paths one after another,
    /// each ending in a null and the whole lot ending in a second one.
    /// </summary>
    private static byte[] DropFiles(IReadOnlyList<string> paths)
    {
        var lists = new List<byte[]>(paths.Count);
        var size = DropHeader;
        foreach (var path in paths)
        {
            var encoded = System.Text.Encoding.Unicode.GetBytes(path + "\0");
            lists.Add(encoded);
            size += encoded.Length;
        }

        var bytes = new byte[size + 2];
        WriteInt(bytes, 0, DropHeader);
        WriteInt(bytes, 16, 1);

        var at = DropHeader;
        foreach (var encoded in lists)
        {
            encoded.CopyTo(bytes, at);
            at += encoded.Length;
        }

        return bytes;
    }

    private static IReadOnlyList<string> WidePaths(byte[] bytes, int start)
    {
        var paths = new List<string>();
        var at = start;
        while (at + 1 < bytes.Length)
        {
            var end = at;
            while (end + 1 < bytes.Length && (bytes[end] != 0 || bytes[end + 1] != 0))
            {
                end += 2;
            }

            if (end == at)
            {
                break;
            }

            paths.Add(System.Text.Encoding.Unicode.GetString(bytes, at, end - at));
            at = end + 2;
        }

        return paths;
    }

    private static IReadOnlyList<string> NarrowPaths(byte[] bytes, int start)
    {
        // An older program can still put a file list on the clipboard in the system code page,
        // so those names are read the same way the file actions read a file of that age.
        var text = TextEncoding.Resolve("gbk");
        var paths = new List<string>();
        var at = start;
        while (at < bytes.Length)
        {
            var end = Array.IndexOf(bytes, (byte)0, at);
            if (end < 0 || end == at)
            {
                break;
            }

            paths.Add(text.GetString(bytes, at, end - at));
            at = end + 1;
        }

        return paths;
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

    private static int ReadInt(byte[] bytes, int at)
        => bytes[at] | bytes[at + 1] << 8 | bytes[at + 2] << 16 | bytes[at + 3] << 24;

    private static void WriteInt(byte[] bytes, int at, int value)
    {
        bytes[at] = (byte)value;
        bytes[at + 1] = (byte)(value >> 8);
        bytes[at + 2] = (byte)(value >> 16);
        bytes[at + 3] = (byte)(value >> 24);
    }

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr memory);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr memory);
}
