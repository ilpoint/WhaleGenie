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

// The notification a step shows, and whether shutting down is allowed.
public sealed partial class WindowsSystemDevice : ISystemDevice
{

    public int Volume() => WindowsAudio.Volume();

    public void SetVolume(int percent) => WindowsAudio.SetVolume(percent);

    public bool IsMuted() => WindowsAudio.Muted();

    public void SetMuted(bool muted) => WindowsAudio.SetMuted(muted);

    public void PlaySound(SoundKind kind) => WindowsAudio.Play(kind);

    public void PlaySoundFile(string path) => WindowsAudio.PlayFile(path);

    /// <summary>
    /// Shows a notification from the icon this program keeps in the notification area. That icon
    /// belongs to the running program rather than to this device, so a program that never put one
    /// there has nowhere to show it and says so instead of quietly doing nothing.
    /// </summary>
    public void Notify(string title, string text, NotificationKind kind)
        => (WindowsTray.Shared ?? throw new DeviceUnavailableException("somewhere to show notifications"))
            .Balloon(title, text, kind);

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
}
