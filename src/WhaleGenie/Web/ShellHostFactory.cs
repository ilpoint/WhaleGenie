using System;
using WhaleGenie.Bridge;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;

namespace WhaleGenie.Web;

/// <summary>/// Builds the session the web shell carries: the native surfaces, the trigger host and the native
/// windows the pages have not taken over. It is a factory because the owner window does not exist
/// when the container is built; the shell asks for its host once it is opening.
/// </summary>
internal sealed class ShellHostFactory(
    ProjectWorkspace workspace,
    SystemState system,
    TriggerRuntime runtime,
    RunQuestionHost questions,
    MenuSurfaceBridge menu,
    WheelSurfaceBridge wheel,
    PanelSurfaceBridge panelFrames,
    AppSettings settings)
{
    public ShellHost Create(WebShellWindow owner)
    {
        menu.Menu.Actions ??= new MenuSurfaceActions(() => owner);

        var actions = new ShellNativeWindows(workspace, () => owner,
            macro =>
            {
                owner.OpenEditor(macro.Name);
                return true;
            },
            macro => owner.OpenRun(macro.Name),
            () => owner.OpenSettings());

        // The menu is the Lit overlay in every build now; the native menu window is gone.
        var bridge = owner.Bridge
            ?? throw new InvalidOperationException("The shell has no bridge to attach the menu to.");
        if (menu.Overlay is null)
        {
            var host = new MenuOverlayHost(bridge, runtime.Devices, settings);
            host.Attach();
            menu.Overlay = host;
        }

        menu.Overlay.Warm();
        var wheelOverlay = new WheelOverlayHost(bridge, wheel);
        wheelOverlay.Warm();
        var shell = new ShellHost(workspace, system, runtime, questions, actions, owner, owner,
            menu.Overlay, wheelOverlay,
            key => new PanelOverlayHost(bridge, panelFrames, key), panelFrames.Surface);
#if DEBUG
        if (App.WheelOverlay)
        {
            wheelOverlay.Open(WheelSurface.Resolve(workspace), new WheelOptions(),
                new OverlayPoint(720, 420), 0);
        }
#endif
#if DEBUG
        if (App.MenuOverlay)
        {
            shell.OpenMenu();
        }
#endif
        return shell;
    }
}
