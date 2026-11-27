using System;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Contracts;

namespace WhaleGenie.Bridge;

/// <summary>
/// The window buttons the page draws: minimize, maximize, close, pin, the title-bar drag and the
/// text-field focus flag. The UI project owns the window and implements the port.
/// </summary>
public sealed class WindowBridge(Func<IWindowCommands?> window,
    Func<IWindowCommands?>? workbench = null) : IBridgeHandler
{
    public void Register(BridgeServer server)
    {
        server.Register("window.minimize", _ => Act(window, static target => target.Minimize()));
        server.Register("window.maximize", _ => Act(window, static target => target.ToggleMaximize()));
        server.Register("window.close", _ => Act(window, static target => target.Close()));
        server.Register("window.pin", _ => Act(window, static target => target.TogglePin()));
        server.Register("window.drag", _ => Act(window, static target => target.BeginDrag()));
        server.Register("window.focus", parameters => Focus(window, parameters));

        // The workbench is a second shell page. Its window buttons use their own names rather than
        // a shared "the active window" guess: a command sent by the workbench must never move the
        // main window, and the other way round.
        if (workbench is null)
        {
            return;
        }

        server.Register("workbench.window.minimize", _ => Act(workbench, static target => target.Minimize()));
        server.Register("workbench.window.maximize", _ => Act(workbench, static target => target.ToggleMaximize()));
        server.Register("workbench.window.close", _ => Act(workbench, static target => target.Close()));
        server.Register("workbench.window.pin", _ => Act(workbench, static target => target.TogglePin()));
        server.Register("workbench.window.drag", _ => Act(workbench, static target => target.BeginDrag()));
        server.Register("workbench.window.focus", parameters => Focus(workbench, parameters));
    }

    /// <summary>
    /// The page tells the window whether a text field has the keyboard. The trigger host asks the
    /// active window before it turns a hotkey into a switch.
    /// </summary>
    private static Task<object?> Focus(Func<IWindowCommands?> target, JsonElement parameters)
    {
        if (parameters.TryGetProperty("textField", out var given)
            && given.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            target()?.SetTextInputFocused(given.GetBoolean());
        }

        return Task.FromResult<object?>(null);
    }

    private static Task<object?> Act(Func<IWindowCommands?> target, Action<IWindowCommands> action)
    {
        if (target() is { } commands)
        {
            action(commands);
        }

        return Task.FromResult<object?>(null);
    }
}
