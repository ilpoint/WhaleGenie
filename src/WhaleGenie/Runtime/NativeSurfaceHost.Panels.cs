using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Avalonia.Threading;
using WhaleGenie.Core.Devices;
using WhaleGenie.Models;

namespace WhaleGenie.Runtime;

// The interface panels beside a window: one overlay per group, placed from the group's rule and
// the target's client area. The page draws them; this part owns which ones are up and where.
internal sealed partial class NativeSurfaceHost
{
    private readonly Dictionary<string, IPanelOverlay> _panels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WindowInfo> _surfaceTargets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WindowRule> _surfaceRules = new(StringComparer.Ordinal);
    private readonly HashSet<string> _hiddenInterfaceGroups = new(StringComparer.Ordinal);

    /// <summary>Builds one group's window; the shell supplies it so this layer stays window-agnostic.</summary>
    private readonly Func<string, IPanelOverlay> _panelFactory;

    /// <summary>The projection whose fold/turn state the page writes and every frame reads.</summary>
    private readonly PanelSurface _panelFrames;

    /// <summary>Windows that moved since the last look. A burst of a drag is answered once.</summary>
    private readonly ConcurrentDictionary<long, byte> _movedWindows = new();

    /// <summary>One bounds refresh at a time, so a window being dragged is not rebuilt per frame.</summary>
    private int _boundsPosted;

    /// <summary>One refresh for the changes that are not about a moving window.</summary>
    private int _refreshSoonPosted;

    private void InitializePanels()
    {
        _devices.WindowEvents.ForegroundChanged += OnForegroundChanged;
        _devices.WindowEvents.BoundsChanged += OnBoundsChanged;
        _workspace.Triggers.Changed += OnProjectChanged;
        _workspace.Changed += OnWorkspaceChanged;
        _panelFrames.ViewChanged += OnPanelViewChanged;
    }

    private void DisposePanels()
    {
        _devices.WindowEvents.ForegroundChanged -= OnForegroundChanged;
        _devices.WindowEvents.BoundsChanged -= OnBoundsChanged;
        _workspace.Triggers.Changed -= OnProjectChanged;
        _workspace.Changed -= OnWorkspaceChanged;
        _panelFrames.ViewChanged -= OnPanelViewChanged;
        HidePanels();
    }

    /// <summary>
    /// The window in front changed. This arrives on the window-event thread, so the menu and every
    /// panel's frame are touched on the interface thread instead; a foreground change is rare
    /// enough that it does not need to ride along with a bounds refresh.
    /// </summary>
    private void OnForegroundChanged()
        => Dispatcher.UIThread.Post(() =>
        {
            CloseMenuForForegroundChange();
            RefreshPanels();
        });

    private void OnBoundsChanged(long handle)
    {
        _movedWindows[handle] = 0;
        if (Interlocked.Exchange(ref _boundsPosted, 1) == 1)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _boundsPosted, 0);
            var moved = _movedWindows.Keys.ToArray();
            foreach (var one in moved)
            {
                _movedWindows.TryRemove(one, out _);
            }

            // A window that moved only matters when it is one of the panels' own targets.
            if (moved.Any(one => _surfaceTargets.Values.Any(target => target.Handle == one)))
            {
                RefreshPanels();
            }
        });
    }

    /// <summary>The project or a panel's fold state changed: the panels may have another shape.</summary>
    private void OnProjectChanged(object? sender, EventArgs e) => RefreshPanelsSoon();

    private void OnWorkspaceChanged(object? sender, EventArgs e) => RefreshPanelsSoon();

    private void OnPanelViewChanged() => RefreshPanelsSoon();

    /// <summary>
    /// A refresh for a change that is not a moving window. It rides along with the one already
    /// waiting rather than queueing behind it; unlike a bounds refresh, it is never dropped, because
    /// there is no handle to decide later whether it mattered.
    /// </summary>
    internal void RefreshPanelsSoon()
    {
        if (Interlocked.Exchange(ref _refreshSoonPosted, 1) == 1)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _refreshSoonPosted, 0);
            RefreshPanels();
        });
    }

    /// <summary>Folds or turns one interface group, and refreshes the panels that carry it.</summary>
    public void ToggleInterfaceGroup(string groupKey)
    {
        if (!_hiddenInterfaceGroups.Add(groupKey))
        {
            _hiddenInterfaceGroups.Remove(groupKey);
        }

        RefreshPanels();
    }

    internal void RefreshPanels()
    {
        // A panel under the pointer is being moved by the system: a refresh would set its place on
        // every frame of the drag and fight the move loop, so the person gets the window back first.
        if (_panels.Values.Any(panel => panel.IsMoving))
        {
            return;
        }

        if (!_system.IsEnabled || MacroTriggerGate.IsOpen || MenuOpen)
        {
            HidePanels();
            return;
        }

        WindowInfo? foreground;
        try
        {
            foreground = _devices.Windows.Foreground();
        }
        catch
        {
            foreground = null;
        }

        var allGroups = _workspace.Triggers.InterfaceGroups();
        var active = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in allGroups)
        {
            if (_hiddenInterfaceGroups.Contains(group.Key))
            {
                HideSurface(group.Key);
                continue;
            }

            var frame = _panelFrames.Snapshot(group.Key, horizontal: false, collapsed: false);
            if (frame.Items.Count == 0)
            {
                HideSurface(group.Key);
                continue;
            }

            var target = SurfaceTargetFor(group, foreground);
            if (target is null)
            {
                HideSurface(group.Key);
                continue;
            }

            active.Add(group.Key);
            if (!_panels.TryGetValue(group.Key, out var panel))
            {
                panel = _panelFactory(group.Key);
                panel.Moved += position => OnPanelMoved(group.Key, position);
                _panels[group.Key] = panel;
                _panelFrames.Attach(group.Key, panel);
            }

            _surfaceTargets[group.Key] = target;
            _surfaceRules[group.Key] = group.Rule;
            ShowPanel(group, frame, target, panel);
        }

        // A group that is no longer in the project takes its window with it; one that is merely not
        // showing keeps its window and page for the next time a target answers to its rule.
        foreach (var key in _panels.Keys.Where(key => !active.Contains(key)).ToList())
        {
            if (allGroups.Any(group => group.Key == key))
            {
                HideSurface(key);
            }
            else
            {
                CloseSurface(key);
            }
        }

        foreach (var key in _surfaceTargets.Keys
                     .Where(key => allGroups.All(group => group.Key != key)).ToList())
        {
            _surfaceTargets.Remove(key);
            _surfaceRules.Remove(key);
        }

        foreach (var gone in _hiddenInterfaceGroups
                     .Where(key => allGroups.All(group => group.Key != key)).ToList())
        {
            _hiddenInterfaceGroups.Remove(gone);
        }
    }

    private void ShowPanel(InterfaceGroupView group, Contracts.SurfacePanelSnapshot frame,
        WindowInfo target, IPanelOverlay panel)
    {
        try
        {
            var layout = _workspace.Triggers.LayoutFor(group.Rule);
            var origin = _devices.Windows.ClientOrigin(target.Handle);
            var client = _devices.Windows.ClientSize(target.Handle);
            var size = PanelGeometry.Size(frame.Items.Count, frame.Horizontal, frame.Collapsed);
            var (x, y) = PanelGeometry.Place((origin.X, origin.Y), (client.Width, client.Height),
                layout.OffsetX, layout.OffsetY, size);
            panel.Show(frame, new OverlayPoint(x, y), size.Width, size.Height, target.Handle);
        }
        catch (Exception error)
        {
            // A target that went away has no client area left to place the panel in. Anything else
            // is a panel that will never appear, which is exactly the kind of silence the next
            // diagnosis cannot read, so it is recorded.
            WhaleGenie.Runtime.AppDiagnostics.Record("panel.show", error);
        }
    }

    private void OnPanelMoved(string groupKey, OverlayPoint position)
    {
        if (!_surfaceTargets.TryGetValue(groupKey, out var target)
            || !_surfaceRules.TryGetValue(groupKey, out var rule))
        {
            return;
        }

        try
        {
            var origin = _devices.Windows.ClientOrigin(target.Handle);
            // The panel reports the place it actually came to rest at, already inside the target;
            // the offset kept is therefore inside the client area as well.
            _workspace.Triggers.StoreLayout(rule, position.X - origin.X, position.Y - origin.Y);
        }
        catch
        {
            // A target that went away has no place to remember.
        }
    }

    private void HidePanels()
    {
        foreach (var key in _panels.Keys.ToList())
        {
            HideSurface(key);
        }
    }

    /// <summary>Takes one panel off screen and forgets what it was sitting beside.</summary>
    private void HideSurface(string key)
    {
        if (_panels.TryGetValue(key, out var panel))
        {
            panel.Hide();
        }

        _surfaceTargets.Remove(key);
        _surfaceRules.Remove(key);
    }

    /// <summary>Gives one panel's window up, because its group is no longer in the project.</summary>
    private void CloseSurface(string key)
    {
        if (_panels.Remove(key, out var panel))
        {
            panel.Hide();
            panel.Close();
            _panelFrames.Detach(key);
        }

        _surfaceTargets.Remove(key);
        _surfaceRules.Remove(key);
    }

    private WindowInfo? SurfaceTargetFor(InterfaceGroupView group, WindowInfo? foreground)
    {
        if (group.Rule.HasTarget
            && _surfaceTargets.TryGetValue(group.Key, out var held)
            && HeldTarget(held, _devices.Windows) is { } kept)
        {
            return kept.Minimized ? null : kept;
        }

        return TargetFor(group.Rule, foreground, _devices.Windows);
    }

    /// <summary>
    /// The window a group's buttons belong to. A rule that names a window keeps the group in that
    /// window while it is open — another program coming to the front does not take the buttons away,
    /// because the buttons are in the target's layer and the window now in front covers them with it.
    /// A rule that names nothing follows whatever is in front, which is its whole meaning.
    /// </summary>
    internal static WindowInfo? TargetFor(WindowRule rule, WindowInfo? foreground,
        IWindowDevice windows)
    {
        if (!rule.HasTarget)
        {
            return foreground;
        }

        try
        {
            var reading = rule.Reading;
            return windows
                .Filter(rule.MatchValue, reading.Match, reading.Compare, everyWhenEmpty: false,
                    rule.Options)
                .FirstOrDefault(window => !window.Minimized
                    && window.Size.Width > 0 && window.Size.Height > 0);
        }
        catch
        {
            // A pattern the machine cannot read, or a device that will not list windows: no target.
            return null;
        }
    }

    /// <summary>
    /// The window a group already belongs to, while it is still open. Once a rule has found a window
    /// the group stays with it until that window closes or is minimized; a title edited after the
    /// rule was written does not move the buttons to another window that happens to match as well.
    /// The window comes back with the size and place it has now, so one moved while hidden is placed
    /// correctly.
    /// </summary>
    internal static WindowInfo? HeldTarget(WindowInfo held, IWindowDevice windows)
    {
        try
        {
            return windows.Find(held.Handle.ToString(CultureInfo.InvariantCulture),
                WindowMatch.Handle, TextCompare.Exactly);
        }
        catch
        {
            return null;
        }
    }
}
