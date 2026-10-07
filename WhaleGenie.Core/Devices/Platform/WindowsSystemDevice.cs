using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace WhaleGenie.Core.Devices.Platform;

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
        "networkconnected" or "networkavailable" => Bool(NetworkInterface.GetIsNetworkAvailable()),
        "networknames" => string.Join(';', Online().Select(adapter => adapter.Name)),
        "localip" => LocalAddress(),
        "batterypercent" => BatteryPercent(),
        "onacpower" => AcLine(),
        "monitorcount" => Monitors().Count.ToString(CultureInfo.InvariantCulture),
        "monitorbounds" => MonitorsText(),
        "dpi" => Dpi().ToString(CultureInfo.InvariantCulture),
        "dpipercent" => ((Dpi() * 100) / 96).ToString(CultureInfo.InvariantCulture),
        "uptimeseconds" => (System.Environment.TickCount64 / 1000)
            .ToString(CultureInfo.InvariantCulture),
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

    public int Volume() => WindowsAudio.Volume();

    public void SetVolume(int percent) => WindowsAudio.SetVolume(percent);

    public bool IsMuted() => WindowsAudio.Muted();

    public void SetMuted(bool muted) => WindowsAudio.SetMuted(muted);

    public void PlaySound(SoundKind kind) => WindowsAudio.Play(kind);

    public string InputMethod() => WindowsKeyboard.Current();

    public IReadOnlyList<string> InputMethods() => WindowsKeyboard.Installed();

    public string? SwitchInputMethod(string layout) => WindowsKeyboard.SwitchTo(layout);

    public int Brightness() => WindowsBrightness.Current();

    public void SetBrightness(int percent) => WindowsBrightness.Set(percent);

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

    private static string Bool(bool value) => value ? "true" : "false";

    /// <summary>
    /// The adapters that are really on a network: up, not a loopback or a tunnel, holding an address
    /// the machine can be reached at, and knowing a way out of its own subnet. Windows lists a lot
    /// besides — the filter drivers and virtual switches that come with Hyper-V, Npcap and the like
    /// all report themselves as up, and not one of them is what a person means by "my network" (this
    /// was measured: eighteen of them on one machine, one of which was the actual cable).
    /// </summary>
    private static IEnumerable<NetworkInterface> Online()
    {
        try
        {
            return [.. NetworkInterface.GetAllNetworkInterfaces().Where(adapter =>
            {
                if (adapter.OperationalStatus != OperationalStatus.Up
                    || adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback
                    or NetworkInterfaceType.Tunnel)
                {
                    return false;
                }

                var properties = adapter.GetIPProperties();
                return properties.GatewayAddresses.Count > 0
                    && properties.UnicastAddresses.Any(address => Reachable(address.Address));
            })];
        }
        catch (NetworkInformationException)
        {
            // A machine whose network stack is unhappy answers "nothing is connected" rather than
            // failing the step: the macro asked a question, and there is an answer to give it.
            return [];
        }
    }

    /// <summary>An address that is really this machine's, rather than one it gave itself.</summary>
    private static bool Reachable(IPAddress address)
        => address.AddressFamily switch
        {
            AddressFamily.InterNetwork => !IPAddress.IsLoopback(address)
                && address.GetAddressBytes() is not [169, 254, ..],
            AddressFamily.InterNetworkV6 => !IPAddress.IsLoopback(address)
                && !address.IsIPv6LinkLocal,
            _ => false,
        };

    /// <summary>The address this machine is reached at, or nothing when it is on no network.</summary>
    private static string LocalAddress()
    {
        foreach (var adapter in Online())
        {
            var found = adapter.GetIPProperties().UnicastAddresses
                .Select(address => address.Address)
                .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork);
            if (found is not null)
            {
                return found.ToString();
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// What the battery is at, or nothing at all on a machine that has none. Windows says "unknown"
    /// rather than zero when there is no battery, and those are not the same answer.
    /// </summary>
    private static string BatteryPercent()
    {
        var status = Power_();
        return status.BatteryLifePercent == Unknown ? string.Empty
            : status.BatteryLifePercent.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Whether the machine is running on mains power rather than its battery.</summary>
    private static string AcLine()
    {
        var status = Power_();
        return status.AcLineStatus == Unknown ? string.Empty : Bool(status.AcLineStatus == 1);
    }

    /// <summary>
    /// Every screen that is switched on, in the order Windows lists them, as one
    /// <c>x,y,width,height</c> per screen joined with semicolons — the same shape the area fields
    /// take, so one can be handed straight to a screen search.
    /// </summary>
    private static string MonitorsText()
        => string.Join(';', Monitors().Select(screen =>
            $"{screen.Left},{screen.Top},{screen.Right - screen.Left},{screen.Bottom - screen.Top}"));

    private static List<Rect> Monitors()
    {
        var found = new List<Rect>();
        _ = EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr _, IntPtr _, ref Rect rect, IntPtr _) =>
            {
                found.Add(rect);
                return true;
            },
            IntPtr.Zero);
        return found;
    }

    /// <summary>The scaling Windows lists the primary screen at: 96 is 100%, 144 is 150%.</summary>
    private static uint Dpi() => OperatingSystem.IsWindows() ? GetDpiForSystem() : 96;

    private static SystemPowerStatus Power_()
        => OperatingSystem.IsWindows() && GetSystemPowerStatus(out var status)
            ? status
            : throw new DeviceUnavailableException("the battery");

    // ---------------------------------------------------------- power, screens, scaling

    /// <summary>What Windows says when it has no answer for one of these numbers.</summary>
    private const byte Unknown = 255;

    /// <summary>A screen's rectangle, which is why the sides are named and not a size.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>What the machine's battery and mains connection are doing.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    private delegate bool MonitorCallback(IntPtr monitor, IntPtr context, ref Rect rect,
        IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumDisplayMonitors(IntPtr context, IntPtr clip,
        MonitorCallback callback, IntPtr data);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

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
