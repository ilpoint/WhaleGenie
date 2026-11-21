using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using WhaleGenie.Models;

namespace WhaleGenie.Runtime;

/// <summary>
/// The session a window carries while the web page is the front end: the trigger host with its
/// menu, wheel and panels, the project session with its recovery draft and closing question, and
/// the native windows the pages have not taken over yet. The main window and the web shell build
/// the same pieces; this is the shell's copy, with no hidden main window behind it.
/// </summary>
internal sealed class ShellHost : IDisposable
{
    private readonly ShellNativeWindows _actions;
    private readonly Window _owner;
    private readonly NativeSurfaceHost _surface;
    private readonly TriggerHost _trigger;
    private readonly ProjectSession _session;

    internal ShellHost(ProjectWorkspace workspace, SystemState system, TriggerRuntime runtime,
        RunQuestionHost questions, ShellNativeWindows actions, Window owner, ITriggerFocusProbe focus)
    {
        _actions = actions;
        _owner = owner;

        _session = new ProjectSession(workspace, () => owner, actions.SaveAsync,
            contents => actions.Restore(contents));
        _session.Watch();

        _surface = new NativeSurfaceHost(workspace, runtime, system, runtime.Devices,
            () => owner, () => _trigger!.Plan, actions);
        _trigger = new TriggerHost(workspace, system, runtime, runtime.Devices, _surface,
            questions, focus);
        _actions.Host = _trigger;
        _trigger.Attach();
        _surface.RefreshForSystemToggle();
    }

    internal ShellNativeWindows Actions => _actions;

    internal ProjectSession Session => _session;

    internal AppTray? Tray
    {
        get => _session.Tray;
        set => _session.Tray = value;
    }

    internal Task OfferRecoveryAsync() => _session.OfferRecoveryAsync();

    internal void OnClosing(WindowClosingEventArgs e) => _session.OnClosing(e, _owner);

    public void Dispose()
    {
        _trigger.Dispose();
        _surface.Dispose();
        _session.Stop();
        _actions.Dispose();
    }
}
