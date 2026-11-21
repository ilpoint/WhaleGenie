using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;

namespace WhaleGenie.Bridge;

/// <summary>
/// What the page knows about the program itself: the version, the project it is showing, and the
/// two things a page must never do by itself — leave through the tray, and open a browser.
/// </summary>
public sealed class AppBridge(ProjectWorkspace workspace, Func<AppTray?> tray) : IBridgeHandler
{
    public void Register(BridgeServer server)
    {
        server.Register("app.info", _ => Task.FromResult<object?>(Info()));
        server.Register("app.quit", _ => QuitAsync());
        server.Register("app.openExternal", OpenExternal);
    }

    /// <summary>
    /// The page's own picture of the program. The project fields are what the title bar and the
    /// home page read; they change with the document, and the change arrives through
    /// <c>project.changed</c> once the project handlers exist.
    /// </summary>
    private object Info() => new
    {
        name = "WhaleGenie",
        version = AppVersion.Text,
        language = LanguageName(),
        debug = IsDebug,
        project = new
        {
            path = workspace.CurrentPath,
            name = workspace.CurrentPath is { } path ? Path.GetFileNameWithoutExtension(path) : null,
            untitled = workspace.CurrentPath is null,
            dirty = workspace.IsDirty,
            revision = workspace.Revision,
            macros = workspace.Macros.Count,
        },
    };

    private Task<object?> QuitAsync()
    {
        // The one true exit goes through the tray, which asks the windows about work that is not
        // saved; a page that wanted to leave the work behind gets asked the same way everything
        // else is.
        tray()?.RequestExit();
        return Task.FromResult<object?>(null);
    }

    private static Task<object?> OpenExternal(JsonElement parameters)
    {
        var url = parameters.TryGetProperty("url", out var given) ? given.GetString() : null;
        if (url is not { } text
            || !Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.OpenExternalNeedsHttp"));
        }

        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        return Task.FromResult<object?>(null);
    }

    private static string LanguageName() => Strings.Current.Language switch
    {
        Language.Chinese => "zh-Hans",
        _ => "en",
    };

#if DEBUG
    private static bool IsDebug => true;
#else
    private static bool IsDebug => false;
#endif
}
