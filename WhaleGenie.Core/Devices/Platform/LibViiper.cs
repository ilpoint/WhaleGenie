using System;
using System.Runtime.InteropServices;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// The C API of libVIIPER: the library that puts a virtual keyboard, mouse and controller on this
/// machine from inside this process.
/// </summary>
/// <remarks>
/// This is the only file in the engine where a name from that library appears. Everything above it
/// talks to <see cref="IViiperNative"/>, which is the same handful of calls with the same shapes, so
/// what the engine asks of the library can be read without the library or the driver on the machine.
///
/// The shapes below are copied from libVIIPER.h of the release this DLL is taken from. Which
/// release that is, where it came from and how to move to another one are in
/// third_party/libVIIPER/README.md; the header itself sits beside it.
/// </remarks>
internal static class LibViiper
{
    /// <summary>What Windows is asked for by name; the file is libVIIPER.dll.</summary>
    internal const string Name = "libVIIPER";

    /// <summary>The modifiers a keyboard report carries in its own byte, in the order HID lists them.</summary>
    internal const byte ModifierLeftCtrl = 0x01;

    internal const byte ModifierLeftShift = 0x02;
    internal const byte ModifierLeftAlt = 0x04;
    internal const byte ModifierLeftGui = 0x08;
    internal const byte ModifierRightCtrl = 0x10;
    internal const byte ModifierRightShift = 0x20;
    internal const byte ModifierRightAlt = 0x40;
    internal const byte ModifierRightGui = 0x80;

    /// <summary>The bits of a mouse report's button byte.</summary>
    internal const byte MouseLeft = 0x01;

    internal const byte MouseRight = 0x02;
    internal const byte MouseMiddle = 0x04;
    internal const byte MouseBack = 0x08;
    internal const byte MouseForward = 0x10;

    /// <summary>The bits of a controller report's button field.</summary>
    internal const uint PadUp = 0x0001;

    internal const uint PadDown = 0x0002;
    internal const uint PadLeft = 0x0004;
    internal const uint PadRight = 0x0008;
    internal const uint PadStart = 0x0010;
    internal const uint PadBack = 0x0020;
    internal const uint PadLeftThumb = 0x0040;
    internal const uint PadRightThumb = 0x0080;
    internal const uint PadLeftShoulder = 0x0100;
    internal const uint PadRightShoulder = 0x0200;
    internal const uint PadGuide = 0x0400;
    internal const uint PadA = 0x1000;
    internal const uint PadB = 0x2000;
    internal const uint PadX = 0x4000;
    internal const uint PadY = 0x8000;

    /// <summary>
    /// What the server is started with. Everything left at zero is the library's own default: how
    /// long a silent connection is kept and how long a device waits to be picked up are the values
    /// it was compiled with, and writes are not batched at all — a report is small and goes out as
    /// soon as it is sent.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct USBServerConfig
    {
        /// <summary>
        /// Where the server listens, as "host:port". It is read once, while the server starts, so
        /// the string only has to live through that call.
        /// </summary>
        [MarshalAs(UnmanagedType.LPStr)] public string Address;

        public ulong ConnectionTimeoutMs;
        public ulong DeviceHandlerConnectTimeoutMs;
        public uint WriteBatchFlushIntervalMs;
    }

    /// <summary>
    /// One keyboard report: which modifiers are held, and which keys are, as one bit per HID usage
    /// code. A report is the whole keyboard rather than a change to it.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardDeviceState
    {
        /// <summary>The modifier bits above, added up.</summary>
        public byte Modifiers;

        /// <summary>256 bits, usage code by usage code: byte <c>usage / 8</c>, bit <c>usage % 8</c>.</summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Keys;
    }

    /// <summary>
    /// One mouse report. The movement and the wheel are how far to go rather than where to end up,
    /// and they are used up by the next poll; the buttons stay as they are until a report says
    /// otherwise.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseDeviceState
    {
        public byte Buttons;
        public short Dx;
        public short Dy;
        public short Wheel;
        public short Pan;
    }

    /// <summary>One controller report: every button and axis together, which is what a pad sends.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Xbox360DeviceState
    {
        public uint Buttons;

        /// <summary>A trigger pulled from 0 to 255.</summary>
        public byte LeftTrigger;

        public byte RightTrigger;

        /// <summary>A stick axis, from the centre, in the range this pad reports.</summary>
        public short LeftX;

        public short LeftY;
        public short RightX;
        public short RightY;

        /// <summary>Bytes the report has room for and the pad does not use.</summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)] public byte[] Reserved;
    }

    [DllImport(Name, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool NewUSBServer(ref USBServerConfig config, out IntPtr server,
        IntPtr logCallback);

    [DllImport(Name, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool CloseUSBServer(IntPtr server);

    [DllImport(Name, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool CreateUSBBus(IntPtr server, ref uint busId);

    [DllImport(Name, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool RemoveUSBBus(IntPtr server, uint busId);

    [DllImport(Name, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool CreateKeyboardDevice(IntPtr server, out IntPtr keyboard, uint busId,
        [MarshalAs(UnmanagedType.I1)] bool autoAttachLocalhost, ushort vendor, ushort product);

    [DllImport(Name, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool SetKeyboardDeviceState(IntPtr keyboard, KeyboardDeviceState state);

    [DllImport(Name, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool RemoveKeyboardDevice(IntPtr keyboard);

    [DllImport(Name, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool CreateMouseDevice(IntPtr server, out IntPtr mouse, uint busId,
        [MarshalAs(UnmanagedType.I1)] bool autoAttachLocalhost, ushort vendor, ushort product);

    [DllImport(Name, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool SetMouseDeviceState(IntPtr mouse, MouseDeviceState state);

    [DllImport(Name, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool RemoveMouseDevice(IntPtr mouse);

    [DllImport(Name, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool CreateXbox360Device(IntPtr server, out IntPtr pad, uint busId,
        [MarshalAs(UnmanagedType.I1)] bool autoAttachLocalhost, ushort vendor, ushort product,
        byte xinputSubType);

    [DllImport(Name, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool SetXbox360DeviceState(IntPtr pad, Xbox360DeviceState state);

    [DllImport(Name, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool RemoveXbox360Device(IntPtr pad);
}
