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

// The network the machine is on, and its power and battery state.
public sealed partial class WindowsSystemDevice : ISystemDevice
{

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

    /// <summary>The scaling Windows lists the primary screen at: 96 is 100%, 144 is 150%.</summary>
    private static uint Dpi() => OperatingSystem.IsWindows() ? GetDpiForSystem() : 96;

    private static SystemPowerStatus Power_()
        => OperatingSystem.IsWindows() && GetSystemPowerStatus(out var status)
            ? status
            : throw new DeviceUnavailableException("the battery");

    // ---------------------------------------------------------- power, screens, scaling

    /// <summary>What Windows says when it has no answer for one of these numbers.</summary>
    private const byte Unknown = 255;

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
}
