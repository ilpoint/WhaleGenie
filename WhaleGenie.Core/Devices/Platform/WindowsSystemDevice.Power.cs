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

// Shutting down, restarting and locking, with the privilege it needs.
public sealed partial class WindowsSystemDevice : ISystemDevice
{

    /// <summary>一个值按文本读出来。写进去的是什么类型，宏那边都当一段文字用。</summary>
    private static string AsText(object? value) => value switch
    {
        null => string.Empty,
        string text => text,
        string[] lines => string.Join(System.Environment.NewLine, lines),

        // A dword comes back as a signed int, and a macro comparing it against 4294967295 would
        // otherwise never recognise the all-bits-set value it just wrote.
        int number => unchecked((uint)number).ToString(CultureInfo.InvariantCulture),
        byte[] bytes => Convert.ToHexString(bytes),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };

    /// <summary>一个 32 位数字，按无符号读：注册表把这一档当 0 到 4294967295。</summary>
    private static int Number(string data)
        => uint.TryParse((data ?? string.Empty).Trim(), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var number)
            ? unchecked((int)number)
            : throw new DeviceActionException(RunKeys.BadRegistryData, data ?? string.Empty);

    /// <summary>
    /// 机器拒绝这次改动。权限与策略不是一个宏作者能当场改掉的东西，所以失败里写的是位置，
    /// 而不是一句「失败了」。不先查「是不是管理员」：真正能不能写，由写本身回答得最准。
    /// </summary>
    private static bool Refused(Exception error)
        => error is UnauthorizedAccessException or System.Security.SecurityException or IOException;

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
                Require(LockWorkStation(), RunKeys.LockRefused);
                return;

            case PowerAction.MonitorOff:
                // The screen comes back the moment anybody touches the mouse or the keyboard, so this
                // one is only ever a screen going dark.
                var told = SendMessageTimeout(
                    Broadcast, SystemCommand, MonitorPower, MonitorOff, AbortIfHung, 2000, out _);
                Require(told != IntPtr.Zero, RunKeys.PowerRefused);
                return;

            case PowerAction.SignOut:
                Require(ExitWindowsEx(LogOff, Planned), RunKeys.PowerRefused);
                return;

            case PowerAction.Sleep:
            case PowerAction.Hibernate:
                // Suspending is a shutdown of the machine's state as far as Windows is concerned, so
                // it wants the same privilege shutting down does whether or not it turns out to be
                // needed. Hibernating on a machine that has the feature switched off fails and says
                // so, rather than quietly sleeping instead.
                _ = AllowShutdown();
                Require(SetSuspendState(action is PowerAction.Hibernate, false, false),
                    RunKeys.PowerRefused);
                return;

            case PowerAction.Restart:
                _ = AllowShutdown();
                Require(InitiateShutdown(null, null, grace, RestartFlags, Planned),
                    RunKeys.PowerRefused);
                return;

            case PowerAction.ShutDown:
                _ = AllowShutdown();
                Require(InitiateShutdown(null, null, grace, PowerOffFlags, Planned),
                    RunKeys.PowerRefused);
                return;

            case PowerAction.AbortShutdown:
                // Calling off a shutdown that is still counting down is always allowed, which is what
                // makes this the way out of a macro that asked for one by mistake.
                Require(AbortSystemShutdown(null), RunKeys.AbortRefused);
                return;

            default:
                throw new DeviceActionException(RunKeys.UnknownPowerAction, action.ToString());
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
}
