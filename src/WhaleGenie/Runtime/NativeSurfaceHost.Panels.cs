using System;
using System.Collections.Generic;
using System.Linq;
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

    private void InitializePanels()
    {
        _devices.WindowEvents.ForegroundChanged += OnForegroundChanged;
        _devices.WindowEvents.BoundsChanged += OnBoundsChanged;
    }

    private void DisposePanels()
    {
        _devices.WindowEvents.ForegroundChanged -= OnForegroundChanged;
        _devices.WindowEvents.BoundsChanged -= OnBoundsChanged;
        HidePanels();
    }

    private void OnForegroundChanged()
        => Dispatcher.UIThread.Post(() =>
        {
            CloseMenuForForegroundChange();
            RefreshPanels();
        });

    private void OnBoundsChanged(long handle)
        => Dispatcher.UIThread.Post(() =>
        {
            if (_surfaceTargets.Values.Any(target => target.Handle == handle))
            {
                RefreshPanels();
            }
        });

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
                RemoveSurface(group.Key);
                continue;
            }

            var frame = _panelFrames.Snapshot(group.Key, horizontal: false, collapsed: false);
            if (frame.Items.Count == 0)
            {
                RemoveSurface(group.Key);
                continue;
            }

            var target = SurfaceTargetFor(group, foreground);
            if (target is null)
            {
                RemoveSurface(group.Key);
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

        foreach (var key in _panels.Keys.Where(key => !active.Contains(key)).ToList())
        {
            RemoveSurface(key);
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
        catch
        {
            // A target that went away has no client area left to place the panel in.
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
            RemoveSurface(key);
        }
    }

    private void RemoveSurface(string key)
    {
        if (_panels.Remove(key, out var panel))
        {
            panel.Hide();
            _panelFrames.Detach(key);
        }

        _surfaceTargets.Remove(key);
        _surfaceRules.Remove(key);
    }

    private WindowInfo? SurfaceTargetFor(InterfaceGroupView group, WindowInfo? foreground)
    {
        if (foreground is not { } target)
        {
            return null;
        }

        return RuleMatches(group.Rule, target) ? target : null;
    }

    private bool RuleMatches(WindowRule rule, WindowInfo foreground)
    {
        try
        {
            return ProjectTriggers.WindowRuleMatches(rule, foreground, _devices.Windows);
        }
        catch
        {
            return false;
        }
    }
}
