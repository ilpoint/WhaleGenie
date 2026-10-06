using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace Viktor.Core.Devices.Platform;

/// <summary>
/// Odds and ends about this machine: who is signed in, where things live, and the handful of
/// things a person can ask it to do to itself from the Start menu's power button.
/// </summary>
public sealed class WindowsSystemDevice : ISystemDevice
{
    public string Info(string field) => field.Trim().ToLowerInvariant() switch
    {
        "username" => System.Environment.UserName,
        "machinename" => System.Environment.MachineName,
        "userdomain" => System.Environment.UserDomainName,
        "osversion" => System.Environment.OSVersion.VersionString,
        "processorcount" => System.Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture),
        "systemfolder" => System.Environment.SystemDirectory,
        "tempfolder" => Trimmed(Path.GetTempPath()),
        "userfolder" => Folder(System.Environment.SpecialFolder.UserProfile),
        "desktopfolder" => Folder(System.Environment.SpecialFolder.DesktopDirectory),
        "documentsfolder" => Folder(System.Environment.SpecialFolder.MyDocuments),
        "downloadsfolder" => Path.Combine(Folder(System.Environment.SpecialFolder.UserProfile), "Downloads"),
        "startupfolder" => Folder(System.Environment.SpecialFolder.Startup),
        "appdatafolder" => Folder(System.Environment.SpecialFolder.ApplicationData),
        "programfiles" => Folder(System.Environment.SpecialFolder.ProgramFiles),
        _ => throw new DeviceActionException("Run.UnknownSystemField", field),
    };

    public string Environment(string name)
        => string.IsNullOrWhiteSpace(name)
            ? throw new DeviceActionException("Run.MissingName")
            : System.Environment.GetEnvironmentVariable(name.Trim()) ?? string.Empty;

    public void Power(PowerAction action, int graceSeconds)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new DeviceUnavailableException("power");
        }

        // A countdown measured in hours would never arrive, and Windows refuses a grace period past
        // its own maximum, so what a macro can ask for is kept inside the range that works.
        var grace = (uint)Math.Clamp(graceSeconds, 0, MaxGraceSeconds);

        switch (action)
        {
            case PowerAction.Lock:
                // Locking needs no privilege and costs nothing: whatever is running keeps running.
                Require(LockWorkStation(), "Run.LockRefused");
                return;

            case PowerAction.MonitorOff:
                // The screen comes back the moment anybody touches the mouse or the keyboard, so this
                // one is only ever a screen going dark.
                var told = SendMessageTimeout(
                    Broadcast, SystemCommand, MonitorPower, MonitorOff, AbortIfHung, 2000, out _);
                Require(told != IntPtr.Zero, "Run.PowerRefused");
                return;

            case PowerAction.SignOut:
                Require(ExitWindowsEx(LogOff, Planned), "Run.PowerRefused");
                return;

            case PowerAction.Sleep:
            case PowerAction.Hibernate:
                // Suspending is a shutdown of the machine's state as far as Windows is concerned, so
                // it wants the same privilege shutting down does whether or not it turns out to be
                // needed. Hibernating on a machine that has the feature switched off fails and says
                // so, rather than quietly sleeping instead.
                _ = AllowShutdown();
                Require(SetSuspendState(action is PowerAction.Hibernate, false, false),
                    "Run.PowerRefused");
                return;

            case PowerAction.Restart:
                _ = AllowShutdown();
                Require(InitiateShutdown(null, null, grace, RestartFlags, Planned),
                    "Run.PowerRefused");
                return;

            case PowerAction.ShutDown:
                _ = AllowShutdown();
                Require(InitiateShutdown(null, null, grace, PowerOffFlags, Planned),
                    "Run.PowerRefused");
                return;

            case PowerAction.AbortShutdown:
                // Calling off a shutdown that is still counting down is always allowed, which is what
                // makes this the way out of a macro that asked for one by mistake.
                Require(AbortSystemShutdown(null), "Run.AbortRefused");
                return;

            default:
                throw new DeviceActionException("Run.UnknownPowerAction", action.ToString());
        }
    }

    /// <summary>Fails the step, with a message the interface translates, when a call says no.</summary>
    private static void Require(bool worked, string key)
    {
        if (!worked)
        {
            throw new DeviceActionException(key);
        }
    }

    /// <summary>
    /// Gives this process the privilege that shutting the machine down needs. Windows hands that
    /// privilege to every user but leaves it switched off, and turning it on is what shutdown.exe
    /// does for itself; a macro that never restarts anything never comes near this.
    /// </summary>
    private static bool AllowShutdown()
    {
        if (!OpenProcessToken(GetCurrentProcess(), AdjustPrivileges | QueryToken, out var token))
        {
            return false;
        }

        try
        {
            if (!LookupPrivilegeValue(null, ShutdownPrivilege, out var luid))
            {
                return false;
            }

            var wanted = new TokenPrivileges
            {
                Count = 1,
                First = new LuidAndAttributes { Privilege = luid, Attributes = PrivilegeEnabled },
            };

            // The size of the structure is not optional: the call needs to be told how much of the
            // buffer it may read, and a zero there turns into a refusal to do anything at all.
            var size = (uint)Marshal.SizeOf<TokenPrivileges>();
            if (!AdjustTokenPrivileges(token, false, ref wanted, size, IntPtr.Zero, IntPtr.Zero))
            {
                return false;
            }

            // AdjustTokenPrivileges answers yes even when it turned nothing on, so the last error is
            // the only way to tell whether the privilege was really there to be turned on.
            return Marshal.GetLastWin32Error() == 0;
        }
        finally
        {
            CloseHandle(token);
        }
    }

    private static string Folder(System.Environment.SpecialFolder folder)
        => System.Environment.GetFolderPath(folder);

    /// <summary>A folder path without the trailing separator, so it joins with a file name.</summary>
    private static string Trimmed(string path)
        => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

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
