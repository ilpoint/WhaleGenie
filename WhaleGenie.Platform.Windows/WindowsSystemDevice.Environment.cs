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


namespace WhaleGenie.Platform.Windows;

// Reading and setting an environment variable.
public sealed partial class WindowsSystemDevice : ISystemDevice
{
    public string Environment(string name)
        => string.IsNullOrWhiteSpace(name)
            ? throw new DeviceActionException(RunKeys.MissingName)
            : System.Environment.GetEnvironmentVariable(name.Trim()) ?? string.Empty;

    /// <summary>
    /// 写一个环境变量，再告诉机器这件事变了。广播只是让资源管理器这类程序把新值读进去，
    /// 已经开始运行的程序仍旧用它启动时拿到的那一份：那是进程环境本来的样子，不是这里能改的。
    /// </summary>
    public string SetEnvironment(string name, string value, EnvironmentScope scope)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new DeviceUnavailableException("environment variables");
        }

        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            throw new DeviceActionException(RunKeys.MissingName);
        }

        if (trimmed.Contains('=') || trimmed.Contains('\0'))
        {
            throw new DeviceActionException(RunKeys.BadEnvironmentName, trimmed);
        }

        var machine = scope is EnvironmentScope.Machine;
        var target = machine ? EnvironmentVariableTarget.Machine : EnvironmentVariableTarget.User;

        try
        {
            System.Environment.SetEnvironmentVariable(trimmed, value, target);
        }
        catch (Exception error) when (Refused(error))
        {
            throw new DeviceActionException(RunKeys.EnvironmentRefused,
                $"{trimmed} ({(machine ? "machine" : "user")})");
        }

        // 变量已经落盘了。广播失败只说明某个顶层窗口没回话，新开的进程照样读得到，所以不把
        // 这一句当成这一步失败。
        _ = AnnounceSetting(Broadcast, SettingChange, IntPtr.Zero, "Environment",
            AbortIfHung, BroadcastTimeoutMs, out _);

        return System.Environment.GetEnvironmentVariable(trimmed, target) ?? string.Empty;
    }
}
