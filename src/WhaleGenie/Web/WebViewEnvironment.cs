using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Platform;
using WhaleGenie.Localization;

namespace WhaleGenie.Web;

/// <summary>
/// The one browser environment every window of this program shares. WebView2 keeps its profiles and
/// caches in a folder of its own under the user's application data: the program folder is often not
/// writable, and without a folder of its own the browser picks one next to whatever host started the
/// program — the checkout's output when it is the exe, a read-only folder under `dotnet` or a
/// debugger. A folder that cannot be made is not a slow page: the control falls back to the legacy
/// WebView1 engine, and every navigation there ends in `E_ABORT`, which is a blank overlay for good.
/// </summary>
internal static class WebViewEnvironment
{
    /// <summary>The options the first window of the process creates the environment with.</summary>
    public static void Configure(WebViewEnvironmentRequestedEventArgs args)
    {
#if DEBUG
        args.EnableDevTools = true;
#endif
        if (args is not WindowsWebView2EnvironmentRequestedEventArgs windows)
        {
            return;
        }

        windows.UserDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WhaleGenie", "webview");
        windows.Language = Strings.Current.Language == Language.Chinese ? "zh-CN" : "en-US";
#if DEBUG
        // The CDP port is what makes the real page automatable: Playwright connects over it the
        // same way it drives a normal browser, which is how the shell checks itself without a person
        // watching. Debug only — a release build exposes no such door.
        windows.AdditionalBrowserArguments = "--remote-debugging-port=9223";
#endif
    }
}
