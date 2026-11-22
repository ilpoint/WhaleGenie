using System;
using WhaleGenie.Bridge;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Web;

/// <summary>
/// Builds the session the web shell carries: the native surfaces, the trigger host and the native
/// windows the pages have not taken over. It is a factory because the owner window does not exist
/// when the container is built; the shell asks for its host once it is opening.
/// </summary>
internal sealed class ShellHostFactory(
    ProjectWorkspace workspace,
    SystemState system,
    TriggerRuntime runtime,
    RunQuestionHost questions,
    MainViewModel viewModel,
    MenuSurfaceBridge menu)
{
    public ShellHost Create(WebShellWindow owner)
    {
        menu.Menu.Actions ??= new MenuSurfaceActions(() => owner, workspace);

        var actions = new ShellNativeWindows(workspace, viewModel, () => owner,
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
            var host = new MenuOverlayHost(bridge, runtime.Devices);
            host.Attach();
            menu.Overlay = host;
        }

        var shell = new ShellHost(workspace, system, runtime, questions, actions, owner, owner,
            menu.Overlay);
#if DEBUG
        if (App.WheelOverlay)
        {
            new WheelOverlayHost(bridge).Show();
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
