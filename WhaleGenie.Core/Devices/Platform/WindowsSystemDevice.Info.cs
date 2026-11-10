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

// What the machine says about itself: idle time, elevation, memory and free space.
public sealed partial class WindowsSystemDevice : ISystemDevice
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
        "dpi" => Dpi().ToString(CultureInfo.InvariantCulture),
        "dpipercent" => ((Dpi() * 100) / 96).ToString(CultureInfo.InvariantCulture),
        "uptimeseconds" => (System.Environment.TickCount64 / 1000)
            .ToString(CultureInfo.InvariantCulture),
        "idleseconds" => IdleSeconds().ToString(CultureInfo.InvariantCulture),
        "iselevated" or "isadmin" => Bool(Elevated()),
        "freerammb" => (Memory().AvailPhys / OneMegabyte).ToString(CultureInfo.InvariantCulture),
        "totalrammb" => (Memory().TotalPhys / OneMegabyte).ToString(CultureInfo.InvariantCulture),
        "freediskmb" => (FreeOnSystemDrive() / OneMegabyte).ToString(CultureInfo.InvariantCulture),
        _ => throw new DeviceActionException(RunKeys.UnknownSystemField, field),
    };

    /// <summary>
    /// 键盘鼠标有多久没被碰过了，按秒算。宏要在人离开的时候才动手，或者反过来——人一直在打字
    /// 就先别抢窗口——问的就是这个。它算的是整个会话里最后那次输入，不看是哪个窗口收到的。
    /// </summary>
    private static uint IdleSeconds()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new DeviceUnavailableException("the input desk");
        }

        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info))
        {
            throw new DeviceUnavailableException("how long the desk has been left alone");
        }

        // 两个数都是开机以来的毫秒数，会在第 49 天回绕；相减仍然对，只要按无符号算。
        return unchecked((uint)System.Environment.TickCount - info.TickCount) / 1000;
    }

    /// <summary>
    /// 这个程序拿的是不是管理员那一份权限。宏要写 Program Files、动服务、改别的用户的设置时，
    /// 先问一句比做到一半报「拒绝访问」好。它看的是当前进程的令牌，不是这个账号在不在管理员组里
    /// ——账号在组里但没提权，能做的事情和不在组里一样多。
    /// </summary>
    private static bool Elevated()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        return IsUserAnAdmin();
    }

    private static MemoryStatus Memory()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new DeviceUnavailableException("the memory");
        }

        // 这个结构体的第一格是它自己的长度，由调用方填：Windows 靠它认版本，别的字段都靠后。
        var status = new GlobalMemoryStatus { Length = (uint)Marshal.SizeOf<GlobalMemoryStatus>() };
        return GlobalMemoryStatusEx(ref status)
            ? new MemoryStatus(status.TotalPhys, status.AvailPhys)
            : throw new DeviceUnavailableException("the memory");
    }

    /// <summary>系统盘还剩多少字节：宏要在写文件之前看一眼，比写到一半没有地方放要好。</summary>
    private static long FreeOnSystemDrive()
    {
        var root = Path.GetPathRoot(System.Environment.SystemDirectory);
        if (string.IsNullOrEmpty(root))
        {
            throw new DeviceUnavailableException("the system drive");
        }

        return new DriveInfo(root).AvailableFreeSpace;
    }
}
