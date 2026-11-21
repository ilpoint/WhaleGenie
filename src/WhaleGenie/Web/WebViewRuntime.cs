using System;
using Microsoft.Win32;

namespace WhaleGenie.Web;

/// <summary>
/// Whether the WebView2 Evergreen runtime is on this machine. Windows 11 ships it and most Windows
/// 10 machines get it with Edge, but a portable copy on a clean Windows 10 can start without it;
/// a shell that tried to build a WebView first would be a white window with no explanation.
/// </summary>
/// <remarks>
/// The runtime registers itself under EdgeUpdate with the WebView2 product id. Per-machine and
/// per-user installations use different hives; the per-machine entry is in the 32-bit view on a
/// 64-bit Windows. The reader is replaceable so a check can answer without touching the registry.
/// </remarks>
public static class WebViewRuntime
{
    /// <summary>Where a machine without the runtime is sent, in the window that explains it.</summary>
    public const string InstallUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    private const string ClientKey =
        @"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";

    /// <summary>The installed runtime's version, or null when there is none.</summary>
    public static Func<string?> VersionReader { get; set; } = ReadVersion;

    public static bool IsInstalled => !string.IsNullOrWhiteSpace(VersionReader());

    private static string? ReadVersion()
    {
        foreach (var (hive, view) in new[]
                 {
                     (RegistryHive.LocalMachine, RegistryView.Registry32),
                     (RegistryHive.LocalMachine, RegistryView.Registry64),
                     (RegistryHive.CurrentUser, RegistryView.Default),
                 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var client = root.OpenSubKey(ClientKey);
                if (client?.GetValue("pv") is string version && version.Length > 0)
                {
                    return version;
                }
            }
            catch
            {
                // A machine that refuses one view may still answer another; the next one tries.
            }
        }

        return null;
    }
}
