using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

public partial class MainWindow : AppWindow
{
    // The floating panel beside the window in front: which interface group it shows, where it sits,
    // and when it goes away. It keeps its own windows so a change to the panel does not read the
    // session, menu or wheel code.
    private readonly Dictionary<string, FloatingSurfaceWindow> _surfaces = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WindowInfo> _surfaceTargets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WindowRule> _surfaceRules = new(StringComparer.Ordinal);

    /// <summary>
    /// How each group was left folded or turned. It outlives the window: a group hidden with its
    /// hotkey and shown again comes back in the shape it was put away in.
    /// </summary>
    private readonly Dictionary<string, FloatingSurfaceWindow.SurfaceViewState> _surfaceViews =
        new(StringComparer.Ordinal);

    private void OnSurfaceForegroundChanged()
        => Dispatcher.UIThread.Post(() =>
        {
            // The menu is the host's now; it knows whether a pinned menu or a child dialog keeps it
            // open. The panels still live here and are refreshed after the question is settled.
            _nativeSurface?.CloseMenuForForegroundChange();
            RefreshSurface(DataContext as MainViewModel);
        });

    private void OnSurfaceBoundsChanged(long handle)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_surfaceTargets.Values.Any(target => target.Handle == handle))
            {
                RefreshSurface(DataContext as MainViewModel);
            }
        });
    }

    private void RefreshSurface(MainViewModel? viewModel)
    {
        if (_surfaces.Values.Any(surface => surface.IsMoving))
        {
            return;
        }

        if (viewModel is null
            || !viewModel.IsRunning
            || MacroTriggerGate.IsOpen
            || _nativeSurface?.MenuOpen == true)
        {
            HideSurface();
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

        var allGroups = viewModel.Triggers.InterfaceGroups();
        var active = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in allGroups)
        {
            if (_hiddenInterfaceGroups.Contains(group.Key))
            {
                // Hiding a group takes its buttons off the screen but not the window they
                // belong to: showing it again puts them back where they were, even if the
                // window has lost the focus in between.
                CloseSurfaceWindow(group.Key);
                continue;
            }

            var macros = InterfaceMemberResolver.Resolve(group, [.. viewModel.Macros])
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
                    if (_triggers is not null && macro.IsEnabled)
                    {
                        _triggers.RunOnce(macro);
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
                surface.PlaceAt(target, _devices.Windows,
                    viewModel.Triggers.LayoutFor(group.Rule));
                if (created)
                {
                    surface.ShowAsPeer(this);
                }

                surface.SitAbove(target.Handle);
            }
        }

        foreach (var key in _surfaces.Keys.Where(key => !active.Contains(key)).ToList())
        {
            RemoveSurface(key);
        }

        // A panel that was closed by a gate or a hide keeps the window it belonged to. Once the
        // group itself is gone there is nothing left to keep it for.
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

    /// <summary>
    /// The handle folded or turned the group: remember it for this session and put the window back
    /// inside the target, because a row of buttons is wider than the column it replaced.
    /// </summary>
    private void OnSurfaceViewChanged(string groupKey, FloatingSurfaceWindow surface,
        FloatingSurfaceWindow.SurfaceViewState state)
    {
        _surfaceViews[groupKey] = state;
        if (!_surfaceTargets.TryGetValue(groupKey, out var target)
            || !_surfaceRules.TryGetValue(groupKey, out var rule)
            || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        try
        {
            surface.PlaceAt(target, _devices.Windows, viewModel.Triggers.LayoutFor(rule));
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
            || !_surfaceRules.TryGetValue(groupKey, out var rule)
            || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        try
        {
            var origin = _devices.Windows.ClientOrigin(target.Handle);
            viewModel.Triggers.StoreLayout(rule, position.X - origin.X, position.Y - origin.Y);
        }
        catch
        {
            // A target that went away has no place to remember.
        }
    }

    private void HideSurface()
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

    /// <summary>
    /// Which window a group's panel belongs to. A group whose rule names a window keeps the one it
    /// was put in, so the panel stays inside that window while another program is in front; it
    /// looks for a new window only once the old one is gone or shrunk. A group with no rule names
    /// nothing, so it belongs to whatever window is in front and moves with it.
    /// </summary>
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

    /// <summary>
    /// The window behind a handle as it stands now, or null once it has gone. The rule is compared
    /// against the window in front itself, not against whatever window happens to be listed first:
    /// the panel is itself a window on top, and asking "the frontmost" would sometimes answer with
    /// that panel rather than with the window it belongs to.
    /// </summary>
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

    /// <summary>The panels on screen, for a check that drives a desktop of its own.</summary>
    internal IReadOnlyCollection<FloatingSurfaceWindow> SurfaceWindowsForCheck => [.. _surfaces.Values];
}
