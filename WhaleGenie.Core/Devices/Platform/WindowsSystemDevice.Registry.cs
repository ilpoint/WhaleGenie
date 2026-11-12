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
using WhaleGenie.Core.Expressions;


namespace WhaleGenie.Core.Devices.Platform;

// Reading, writing and deleting a registry value, and the paths it accepts.
public sealed partial class WindowsSystemDevice : ISystemDevice
{
    public string ReadRegistry(RegistryBranch branch, string path, string valueName)
    {
        var keyPath = RegistryPath(path);
        try
        {
            using var key = Root(branch).OpenSubKey(keyPath);
            return key is null
                ? string.Empty
                : AsText(key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames));
        }
        catch (Exception error) when (Refused(error))
        {
            throw new DeviceActionException(RunKeys.RegistryRefused, Whole(branch, keyPath));
        }
    }

    public string WriteRegistry(RegistryBranch branch, string path, string valueName,
        RegistryValueType type, string data)
    {
        var keyPath = RegistryPath(path);
        try
        {
            using var key = Root(branch).CreateSubKey(keyPath, writable: true)
                ?? throw new DeviceActionException(RunKeys.RegistryRefused, Whole(branch, keyPath));

            switch (type)
            {
                case RegistryValueType.DWord:
                    key.SetValue(valueName, Number(data), RegistryValueKind.DWord);
                    break;

                case RegistryValueType.ExpandString:
                    key.SetValue(valueName, data, RegistryValueKind.ExpandString);
                    break;

                default:
                    key.SetValue(valueName, data, RegistryValueKind.String);
                    break;
            }

            return AsText(key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames));
        }
        catch (Exception error) when (Refused(error))
        {
            throw new DeviceActionException(RunKeys.RegistryRefused, Whole(branch, keyPath));
        }
    }

    public void DeleteRegistry(RegistryBranch branch, string path, string valueName)
    {
        var keyPath = RegistryPath(path);
        try
        {
            using var key = Root(branch).OpenSubKey(keyPath, writable: true);
            key?.DeleteValue(valueName, throwOnMissingValue: false);
        }
        catch (Exception error) when (Refused(error))
        {
            throw new DeviceActionException(RunKeys.RegistryRefused, Whole(branch, keyPath));
        }
    }

    /// <summary>
    /// 哪一处根。这里只有当前用户与这台机器两条路：宏改得动的东西要能被下一次运行改回来，
    /// 而 HKCR、HKU 那一类是「整台机器的每个程序都照它办」的地方。
    /// </summary>
    private static RegistryKey Root(RegistryBranch branch)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new DeviceUnavailableException("the registry");
        }

        return branch is RegistryBranch.LocalMachine
            ? Microsoft.Win32.Registry.LocalMachine
            : Microsoft.Win32.Registry.CurrentUser;
    }

    /// <summary>完整位置，失败时写在说明里，读的人才知道是哪一处被拒了。</summary>
    private static string Whole(RegistryBranch branch, string path)
        => $"{(branch is RegistryBranch.LocalMachine ? "HKEY_LOCAL_MACHINE" : "HKEY_CURRENT_USER")}\\{path}";

    /// <summary>
    /// 路径要写成什么样子。注册表把路径当一串名字，`..` 在这里并没有跳出去的意思，可是写的人
    /// 会以为有；带根前缀的路径则会让「默认只碰当前用户」变成一句空话。这两样连同空段、斜杠
    /// 一起拒绝，说的都是同一件事：这一格要的是根下面的一段路。
    /// </summary>
    internal static string RegistryPath(string path)
    {
        var written = (path ?? string.Empty).Trim();
        if (written.Length == 0)
        {
            throw new DeviceActionException(RunKeys.MissingPath);
        }

        var misplaced =
            written.StartsWith('\\')
            || written.Contains('/', StringComparison.Ordinal)
            || written.Contains(':', StringComparison.Ordinal)
            || written.StartsWith("HKEY_", StringComparison.OrdinalIgnoreCase)
            || written.StartsWith("HKCU", StringComparison.OrdinalIgnoreCase)
            || written.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase)
            || written.Split('\\').Any(part => part.Length == 0 || part is "." or "..");

        return misplaced
            ? throw new DeviceActionException(RunKeys.BadRegistryName, written)
            : written;
    }
}
