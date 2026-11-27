using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Contracts;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;

namespace WhaleGenie.Bridge;

/// <summary>
/// The temporary doors from the page to the native windows that have not moved yet. Each one
/// disappears when its page arrives; the manual opens through a port because it is a native window.
/// </summary>
internal sealed class ShellBridge(ProjectWorkspace workspace, IShellCommands shell,
    Func<IWorkbenchHost?>? workbench = null) : IBridgeHandler
{
    private BridgeServer? _server;

    public void Register(BridgeServer server)
    {
        _server = server;
        server.Register("app.openRun", OpenRun);
        server.Register("app.openEditor", OpenEditor);
        server.Register("app.newEditor", NewEditor);
        server.Register("workbench.focusMain", _ => FocusMain());
        server.Register("app.openManual", _ => OpenManual());
    }

    private Task<object?> OpenEditor(JsonElement parameters)
    {
        var name = parameters.TryGetProperty("name", out var given) ? given.GetString() : null;
        workbench?.Invoke()?.OpenEditor(name);
        return Task.FromResult<object?>(null);
    }

    private Task<object?> NewEditor(JsonElement parameters)
    {
        workbench?.Invoke()?.OpenEditor(string.Empty);
        return Task.FromResult<object?>(null);
    }

    private Task<object?> FocusMain()
    {
        workbench?.Invoke()?.FocusMain();
        return Task.FromResult<object?>(null);
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

        // Every run entry now lands in the web panel; an unnamed request has no macro to load.
        if (macro is null || _server is null)
        {
            throw new BridgeFailureException(BridgeErrors.NoSuchMacro,
                Strings.Get("Bridge.NoSuchMacro"));
        }

        if (workbench?.Invoke() is { } host)
        {
            host.OpenRun(macro.Name);
        }
        else
        {
            await _server.PushAsync("run.openRequested", new { name = macro.Name },
                BridgePushMode.Latest, BridgeSurface.Workbench);
        }

        return null;
    }

    private Task<object?> OpenManual()
    {
        shell.OpenManual();
        return Task.FromResult<object?>(null);
    }
}
