using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Viktor.Core.Devices.Platform;

/// <summary>How far this machine is from taking input that looks like real hardware.</summary>
public enum DriverInputState
{
    /// <summary>The kernel driver is installed and a VIIPER server is answering.</summary>
    Ready,

    /// <summary>No usbip-win2 kernel driver, so there is no virtual USB device to send into.</summary>
    DriverMissing,

    /// <summary>The driver is there, but nothing is answering where the VIIPER server should be.</summary>
    ServerMissing,
}

/// <summary>
/// Driver-level input rests on two pieces Viktor neither ships nor installs by itself: the
/// usbip-win2 kernel driver and a running VIIPER server. Both belong to their own projects and both
/// need the user's say-so before they go on the machine, so all Viktor does is look — the settings
/// window turns that look into "install this" or "run that".
/// </summary>
public static class DriverInput
{
    /// <summary>
    /// The service the usbip-win2 driver registers, which is what "installed" means. The version is
    /// the installer's business and is deliberately not written down here, where it would only be
    /// wrong the day after that project released again.
    /// </summary>
    private const string DriverService = @"SYSTEM\CurrentControlSet\Services\usbip2_ude";

    /// <summary>The port the VIIPER server answers its clients on, on this machine only.</summary>
    private const int ServerPort = 3242;

    /// <summary>How long a probe waits for an answer before it counts as nothing being there.</summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(400);

    /// <summary>Looks at this machine and says which piece driver-level input is still missing.</summary>
    public static DriverInputState Check()
    {
        if (!DriverInstalled())
        {
            return DriverInputState.DriverMissing;
        }

        return ServerListening() ? DriverInputState.Ready : DriverInputState.ServerMissing;
    }

    private static bool DriverInstalled()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var service = Registry.LocalMachine.OpenSubKey(DriverService);
            return service is not null;
        }
        catch
        {
            // A key that cannot be read is a driver that cannot be counted on.
            return false;
        }
    }

    private static bool ServerListening()
    {
        // The connect is kept off this thread so the window that asked never waits on a server that
        // is not there, and the timeout is what turns a refusal into a quick "no" instead of
        // Windows' own minute-long connect wait.
        var probe = Task.Run(() =>
        {
            try
            {
                using var client = new TcpClient();
                client.Connect(IPAddress.Loopback, ServerPort);
                return true;
            }
            catch
            {
                return false;
            }
        });

        return probe.Wait(ProbeTimeout) && probe.Result;
    }
}
