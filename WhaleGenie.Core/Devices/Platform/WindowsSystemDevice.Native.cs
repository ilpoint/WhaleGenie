using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Win32;


namespace WhaleGenie.Core.Devices.Platform;

// The structures and calls the system answers through.
public sealed partial class WindowsSystemDevice : ISystemDevice
{

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    // ------------------------------------------------- idle, memory, rights, disk

    private const long OneMegabyte = 1024L * 1024L;

    /// <summary>How long the keyboard and mouse have been left alone, in the session's own clock.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint TickCount;
    }

    private readonly record struct MemoryStatus(ulong TotalPhys, ulong AvailPhys);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    [DllImport("shell32.dll")]
    private static extern bool IsUserAnAdmin();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref GlobalMemoryStatus status);

    /// <summary>The memory figures Windows reports, in bytes; the fields after these are not read.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct GlobalMemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    // ------------------------------------------------------------- the power button

    /// <summary>How long a countdown Windows is willing to run before it gives up on it.</summary>
    private const int MaxGraceSeconds = 600;

    /// <summary>Windows' own note in the event log: no reason given, and the shutdown was planned.</summary>
    private const uint Planned = 0x80000000;

    private const uint LogOff = 0x00000000;
    private const uint RestartFlags = 0x00000004;
    private const uint PowerOffFlags = 0x00000008;
    private const uint AdjustPrivileges = 0x0020;
    private const uint QueryToken = 0x0008;
    private const uint PrivilegeEnabled = 0x00000002;
    private const string ShutdownPrivilege = "SeShutdownPrivilege";
    private const uint SystemCommand = 0x0112;
    private const uint AbortIfHung = 0x0002;
    private const uint SettingChange = 0x001A;
    private const uint BroadcastTimeoutMs = 3000;
    private static readonly IntPtr Broadcast = new(-1);
    private static readonly IntPtr MonitorPower = new(0xF170);
    private static readonly IntPtr MonitorOff = new(2);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool LockWorkStation();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ExitWindowsEx(uint flags, uint reason);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr first,
        IntPtr second, uint flags, uint timeout, out IntPtr result);

    /// <summary>
    /// 同一个广播，只是第二个参数是一个字符串而不是数：告诉机器「设置变了」时，lParam 指向
    /// 变的是哪一类，这里是 "Environment" 那串字本身。
    /// </summary>
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode,
        EntryPoint = "SendMessageTimeoutW")]
    private static extern IntPtr AnnounceSetting(IntPtr window, uint message, IntPtr first,
        string second, uint flags, uint timeout, out IntPtr result);

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern bool SetSuspendState(bool hibernate, bool force, bool disableWakeEvent);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool InitiateShutdown(string? machine, string? message, uint graceSeconds,
        uint flags, uint reason);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool AbortSystemShutdown(string? machine);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string? machine, string name,
        out PrivilegeId luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll,
        ref TokenPrivileges wanted, uint size, IntPtr previous, IntPtr written);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    /// <summary>One privilege, in the shape the token calls want it.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct LuidAndAttributes
    {
        public PrivilegeId Privilege;
        public uint Attributes;
    }

    /// <summary>
    /// A privilege, numbered the way Windows numbers them: two 32-bit halves. Written out as the two
    /// halves rather than as one 64-bit number because that is what Windows means by it — a 64-bit
    /// field here would put four bytes of padding in front of the number, and Windows would read a
    /// privilege nobody asked for and quietly do nothing (this was measured: it answers "not all
    /// privileges were assigned" with the right number in the wrong place).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct PrivilegeId
    {
        public uint Low;
        public int High;
    }

    /// <summary>A list of one, which is all this ever asks for.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges
    {
        public uint Count;
        public LuidAndAttributes First;
    }
}
