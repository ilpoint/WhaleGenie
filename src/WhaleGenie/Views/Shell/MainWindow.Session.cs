using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

public partial class MainWindow : AppWindow
{
    // The session around the project: the pin, the remembered window size, leaving with unsaved
    // work, and the recovery snapshot written while the project changes. The rules live in
    // ProjectSession; this window supplies the save picker and the dialog owner.
    private ProjectSession? _session;
    private AppTray? _pendingTray;

    /// <summary>Whether the pin on the menu bar is holding the window above the other windows.</summary>
    private bool _alwaysOnTop;

    /// <summary>
    /// The icon in the notification area, or null when this machine has none. It is what tells a
    /// close that leaves the program apart from a close that only puts the window out of the way.
    /// </summary>
    internal AppTray? Tray
    {
        get => _session?.Tray ?? _pendingTray;
        set
        {
            _pendingTray = value;
            if (_session is { } session)
            {
                session.Tray = value;
            }
        }
    }

    /// <summary>The session for this window, made when the document is known.</summary>
    private ProjectSession Session => _session ??= CreateSession();

    private ProjectSession CreateSession()
    {
        if (DataContext is not MainViewModel viewModel)
        {
            throw new InvalidOperationException("the main window has no project to keep a session for");
        }

        var session = new ProjectSession(viewModel.Workspace, () => this,
            () => SaveProjectAsync(viewModel), contents => Recover(viewModel, contents));
        session.Tray = _pendingTray;
        return session;
    }

    /// <summary>
    /// Puts the question about the unsaved project to the user and reports what they chose. It is
    /// a property so that a check can answer without a dialog standing between it and the decision;
    /// nothing else replaces it.
    /// </summary>
    internal Func<Task<ConfirmChoice>> AskToSaveProject
    {
        get => Session.AskToSaveProject;
        set => Session.AskToSaveProject = value;
    }

    /// <summary>
    /// Offers back the work a stopped run left behind and reports what the user chose. A property
    /// for the same reason as AskToSaveProject: a check can answer without a dialog.
    /// </summary>
    internal Func<RecoveryContents, Task<ConfirmChoice>> AskToRecover
    {
        get => Session.AskToRecover;
        set => Session.AskToRecover = value;
    }

    /// <summary>Keeps the main window above the other windows until the pin is pressed again.</summary>
    private void ToggleAlwaysOnTop(Button button)
    {
        _alwaysOnTop = !_alwaysOnTop;
        Topmost = _alwaysOnTop;

        // Two ways to read the state: the icon changes (Pin when free, PinOff when pinned) and the
        // button takes the accent while it is on. The hint says what the next press does.
        button.Classes.Set("pinned", _alwaysOnTop);
        if (button.Content is IconView icon)
        {
            icon.Data = _alwaysOnTop ? AppIcons.PinOff : AppIcons.Pin;
        }

        ToolTip.SetTip(button, Strings.Get(_alwaysOnTop ? "Main.AlwaysOnTopCancel" : "Main.AlwaysOnTop"));
    }

    /// <summary>
    /// Applies the size this machine last chose, limited to the desktop that is here now. Position
    /// is deliberately not restored: a monitor may have gone, and a window must not open off-screen.
    /// </summary>
    private void RestoreWindowSize() => WindowPlacement.Restore(this);

    /// <summary>Remembers the ordinary size; a maximized or minimized size is not a choice.</summary>
    private void RememberWindowSize() => WindowPlacement.Remember(this);

    /// <summary>
    /// Asks about the project before the program stops. A plain close is not the moment for this:
    /// the window only goes out of the way, and everything in it is still there to be saved later.
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        RememberWindowSize();

        if (_session is { } session)
        {
            session.OnClosing(e, this);
        }

        base.OnClosing(e);
    }

    /// <summary>Offers back what the last run left unsaved; once per run.</summary>
    internal Task OfferRecoveryAsync() => _session?.OfferRecoveryAsync() ?? Task.CompletedTask;

    /// <summary>Starts watching the document; called when the window opens.</summary>
    private void WatchSession(MainViewModel viewModel)
    {
        _session = Session;
        _session.Watch();
        viewModel.Variables.GlobalsChanged += OnGlobalsChanged;
    }
}
