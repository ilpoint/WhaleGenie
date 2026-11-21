using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;

namespace WhaleGenie.Bridge;

/// <summary>
/// The temporary doors from the page to the native windows that have not moved yet. They are
/// deliberately few: settings, run-and-debug and about/manual. Each one disappears when its page
/// arrives; until then the page says which window it is asking for instead of pretending to own it.
/// </summary>
internal sealed class ShellBridge(
    ProjectWorkspace workspace,
    Func<ShellNativeWindows?> windows) : IBridgeHandler
{
    public void Register(BridgeServer server)
    {
        server.Register("app.openSettings", _ => OpenSettings());
        server.Register("app.openRun", OpenRun);
        server.Register("app.openAbout", _ => OpenAbout());
        server.Register("app.openManual", _ => OpenManual());
    }

    private async Task<object?> OpenSettings()
    {
        if (windows() is { } actions)
        {
            await actions.OpenSettingsAsync();
        }

        return null;
    }

    private async Task<object?> OpenRun(JsonElement parameters)
    {
        MacroItem? macro = null;
        if (parameters.TryGetProperty("name", out var named) && named.GetString() is { Length: > 0 } wanted)
        {
            macro = workspace.Macros.FirstOrDefault(candidate =>
                string.Equals(candidate.Name.Trim(), wanted.Trim(), StringComparison.OrdinalIgnoreCase));
            if (macro is null)
            {
                throw new BridgeFailureException(BridgeErrors.NoSuchMacro,
                    Strings.Format("Bridge.NoSuchMacro", wanted));
            }
        }

        if (windows() is { } actions)
        {
            await actions.OpenRunAsync(macro);
        }

        return null;
    }

    private async Task<object?> OpenAbout()
    {
        if (windows() is { } actions)
        {
            await actions.OpenAboutAsync();
        }

        return null;
    }

    private Task<object?> OpenManual()
    {
        ShellNativeWindows.OpenManual();
        return Task.FromResult<object?>(null);
    }
}
