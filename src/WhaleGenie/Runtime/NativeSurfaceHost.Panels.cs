using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Threading;
using WhaleGenie.Core.Devices;
using WhaleGenie.Models;
using WhaleGenie.Views;

namespace WhaleGenie.Runtime;

// The floating panel beside the window in front: which interface group it shows, where it sits,
// and when it goes away. It reads the project and the trigger service directly and owns the
// foreground/bounds subscriptions, so it keeps working when the web shell is the visible window.
internal sealed partial class NativeSurfaceHost
{
    private readonly Dictionary<string, FloatingSurfaceWindow> _surfaces = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WindowInfo> _surfaceTargets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WindowRule> _surfaceRules = new(StringComparer.Ordinal);
    private readonly HashSet<string> _hiddenInterfaceGroups = new(StringComparer.Ordinal);

    /// <summary>How each group was left folded or turned; outlives one panel window.</summary>
    private readonly Dictionary<string, FloatingSurfaceWindow.SurfaceViewState> _surfaceViews =
        new(StringComparer.Ordinal);

    internal IReadOnlyCollection<FloatingSurfaceWindow> SurfaceWindowsForCheck => [.. _surfaces.Values];

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
        if (_surfaces.Values.Any(surface => surface.IsMoving))
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
                // Hiding a group takes its buttons off the screen but not the window they belong
                // to; showing it again puts them back where they were.
                CloseSurfaceWindow(group.Key);
                continue;
            }

            var macros = InterfaceMemberResolver.Resolve(group, [.. _workspace.Macros])
                .Where(member => member.IsVisibleOnSurface)
                .Select(member => member.Macro!)
                .ToList();
            if (macros.Count == 0)
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
            var created = false;
            if (!_surfaces.TryGetValue(group.Key, out var surface))
            {
                var view = _surfaceViews.TryGetValue(group.Key, out var remembered)
                    ? remembered
                    : default;
                surface = new FloatingSurfaceWindow(macros, macro =>
                {
                    if (macro.IsEnabled)
                    {
                        _runtime.Triggers.RunOnce(macro);
                    }
                }, view);
                surface.MoveCompleted += position => OnSurfaceMoved(group.Key, position);
                surface.ViewChanged += state => OnSurfaceViewChanged(group.Key, surface, state);
                _surfaces[group.Key] = surface;
                created = true;
            }
            else
            {
                surface.SetMacros(macros);
            }

            _surfaceTargets[group.Key] = target;
            _surfaceRules[group.Key] = group.Rule;
            if (!surface.IsMoving)
            {
                surface.PlaceAt(target, _devices.Windows, _workspace.Triggers.LayoutFor(group.Rule));
                if (created)
                {
                    if (_owner() is { } owner)
                    {
                        surface.ShowAsPeer(owner);
                    }
                    else
                    {
                        surface.Show();
                    }
                }

                surface.SitAbove(target.Handle);
            }
        }

        foreach (var key in _surfaces.Keys.Where(key => !active.Contains(key)).ToList())
        {
            RemoveSurface(key);
        }

        foreach (var key in _surfaceTargets.Keys
                     .Where(key => allGroups.All(group => group.Key != key)).ToList())
        {
            _surfaceTargets.Remove(key);
            _surfaceRules.Remove(key);
            _surfaceViews.Remove(key);
        }

        foreach (var gone in _hiddenInterfaceGroups
                     .Where(key => allGroups.All(group => group.Key != key)).ToList())
        {
            _hiddenInterfaceGroups.Remove(gone);
        }
    }

    private void OnSurfaceViewChanged(string groupKey, FloatingSurfaceWindow surface,
        FloatingSurfaceWindow.SurfaceViewState state)
    {
        _surfaceViews[groupKey] = state;
        if (!_surfaceTargets.TryGetValue(groupKey, out var target)
            || !_surfaceRules.TryGetValue(groupKey, out var rule))
        {
            return;
        }

        try
        {
            surface.PlaceAt(target, _devices.Windows, _workspace.Triggers.LayoutFor(rule));
            surface.SitAbove(target.Handle);
        }
        catch
        {
            // A target that went away has no client area left to place the panel in.
        }
    }

    private void OnSurfaceMoved(string groupKey, PixelPoint position)
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
        foreach (var key in _surfaces.Keys.ToList())
        {
            CloseSurfaceWindow(key);
        }
    }

    private void CloseSurfaceWindow(string key)
    {
        if (_surfaces.Remove(key, out var surface))
        {
            surface.Close();
        }
    }

    private void RemoveSurface(string key)
    {
        CloseSurfaceWindow(key);
        _surfaceTargets.Remove(key);
        _surfaceRules.Remove(key);
    }

    private WindowInfo? SurfaceTargetFor(InterfaceGroupView group, WindowInfo? foreground)
    {
        if (!group.Rule.HasTarget)
        {
            return foreground is { Minimized: false } ? foreground : null;
        }

        if (_surfaceTargets.TryGetValue(group.Key, out var held)
            && LivingWindow(held.Handle) is { Minimized: false } living)
        {
            return living;
        }

        _surfaceTargets.Remove(group.Key);
        return foreground is { Minimized: false } && RuleMatches(group.Rule, foreground)
            ? foreground
            : null;
    }

    private WindowInfo? LivingWindow(long handle)
    {
        try
        {
            return _devices.Windows.Find(handle.ToString(CultureInfo.InvariantCulture),
                WindowMatch.Handle, TextCompare.Exactly);
        }
        catch
        {
            return null;
        }
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
