using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Contracts;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;

namespace WhaleGenie.Bridge;

/// <summary>
/// What the page knows about the program itself: the version and the language, plus the two things
/// a page must never do by itself — leave through the tray, and open a browser. The open project is
/// not repeated here; <c>project.state</c> is the one shape that carries it.
/// </summary>
public sealed class AppBridge(Func<IAppExit?> exit) : IBridgeHandler
{
    public void Register(BridgeServer server)
    {
        server.Register("app.info", _ => Task.FromResult<object?>(Info()));
        server.Register("app.about", _ => Task.FromResult<object?>(About()));
        server.Register("app.quit", _ => QuitAsync());
        server.Register("app.openExternal", OpenExternal);
    }

    /// <summary>
    /// The page's own picture of the program. The project fields are what the title bar and the
    /// home page read; they change with the document, and the change arrives through
    /// <c>project.changed</c> once the project handlers exist.
    /// </summary>
    private static AppInfo Info()
        => new("WhaleGenie", AppVersion.Text, LanguageName(), IsDebug);

    /// <summary>
    /// The two things the About page cannot know by itself: where the project lives and the
    /// third-party notice packed into this build. The page formats the version line from app.info,
    /// so both pages always show the same build.
    /// </summary>
    private static AboutState About()
        => new(ProjectLinks.Home, ThirdPartyNotices.Read());

    private Task<object?> QuitAsync()
    {
        // The one true exit goes through the tray, which asks the windows about work that is not
        // saved; a page that wanted to leave the work behind gets asked the same way everything
        // else is.
        exit()?.RequestExit();
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
