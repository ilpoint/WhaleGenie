using System;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Contracts;

namespace WhaleGenie.Bridge;

/// <summary>
/// The window buttons the page draws: minimize, maximize, close, pin, the title-bar drag and the
/// text-field focus flag. The UI project owns the window and implements the port.
/// </summary>
public sealed class WindowBridge(Func<IWindowCommands?> window) : IBridgeHandler
{
    public void Register(BridgeServer server)
    {
        server.Register("window.minimize", _ => Act(static target => target.Minimize()));
        server.Register("window.maximize", _ => Act(static target => target.ToggleMaximize()));
        server.Register("window.close", _ => Act(static target => target.Close()));
        server.Register("window.pin", _ => Act(static target => target.TogglePin()));
        server.Register("window.drag", _ => Act(static target => target.BeginDrag()));
        server.Register("window.focus", Focus);
    }

    /// <summary>
    /// The page tells the window whether a text field has the keyboard. The trigger host asks the
    /// active window before it turns a hotkey into a switch.
    /// </summary>
    private Task<object?> Focus(JsonElement parameters)
    {
        if (parameters.TryGetProperty("textField", out var given)
            && given.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            window()?.SetTextInputFocused(given.GetBoolean());
        }

        return Task.FromResult<object?>(null);
    }

    private Task<object?> Act(Action<IWindowCommands> action)
    {
        if (window() is { } target)
        {
            action(target);
        }

        return Task.FromResult<object?>(null);
    }
}
