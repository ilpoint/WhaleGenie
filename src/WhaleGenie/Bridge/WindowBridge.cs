using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using WhaleGenie.Web;

namespace WhaleGenie.Bridge;

/// <summary>
/// The window buttons the page draws. The page owns the pixels; this owns the window: minimize,
/// maximize, close (which the tray turns into hiding), pin and the title-bar drag.
/// </summary>
/// <remarks>
/// The window is handed over as a factory rather than an instance, because the container builds
/// the handlers before the shell window exists, and the page calls these only once it is up. The
/// drag belongs to the shell window itself — it is a Win32 caption move, not an ordinary window
/// operation.
/// </remarks>
public sealed class WindowBridge(Func<WebShellWindow?> window) : IBridgeHandler
{
    public void Register(BridgeServer server)
    {
        server.Register("window.minimize", _ => Act(static target => target.WindowState = WindowState.Minimized));
        server.Register("window.maximize", _ => Act(ToggleMaximize));
        server.Register("window.close", _ => Act(static target => target.Close()));
        server.Register("window.pin", _ => Act(target => target.Topmost = !target.Topmost));
        server.Register("window.drag", _ => Act(static target => target.BeginNativeDrag()));
    }

    private Task<object?> Act(Action<WebShellWindow> action)
    {
        if (window() is not { } target)
        {
            return Task.FromResult<object?>(null);
        }

        action(target);
        return Task.FromResult<object?>(null);
    }

    private static void ToggleMaximize(Window target)
        => target.WindowState = target.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
}
