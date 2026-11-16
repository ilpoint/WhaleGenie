using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
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

    private void OnSurfaceForegroundChanged()
        => Dispatcher.UIThread.Post(() =>
        {
            if (_menu is { IsPinned: false } menu
                && _menuForeground is { } expected
                && CanCloseMenuFromOutside(
                    Volatile.Read(ref _menuChildOpen),
                    Volatile.Read(ref _menuPopupOpen)))
            {
                try
                {
                    if (SharedDevices.Current.Windows.Foreground() is { } current
                        && current.Handle != expected.Handle
                        && current.Handle != menu.NativeHandle.ToInt64())
                    {
                        if (menu.IsSearchFocused)
                        {
                            menu.CloseFromOutside();
                        }
                        else
                        {
                            menu.CloseFromLauncher();
                        }
                    }
                }
                catch
                {
                    // The next foreground event can close it when the answer is readable.
                }
            }

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
            || _menu is not null)
        {
            HideSurface();
            return;
        }

        WindowInfo? foreground;
        try
        {
            foreground = SharedDevices.Current.Windows.Foreground();
        }
        catch
        {
            HideSurface();
            return;
        }

        if (foreground is null || foreground.Minimized)
        {
            HideSurface();
            return;
        }

        var allGroups = viewModel.Triggers.InterfaceGroups();
        var groups = allGroups
            .Where(group => ForegroundRuleMatches(group.Rule, foreground))
            .ToList();
        var active = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            if (_hiddenInterfaceGroups.Contains(group.Key))
            {
                RemoveSurface(group.Key);
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

            active.Add(group.Key);
            var created = false;
            if (!_surfaces.TryGetValue(group.Key, out var surface))
            {
                surface = new FloatingSurfaceWindow(macros, macro =>
                {
                    if (_triggers is not null && macro.IsEnabled)
                    {
                        _triggers.RunOnce(macro);
                    }
                });
                surface.MoveCompleted += position => OnSurfaceMoved(group.Key, position);
                _surfaces[group.Key] = surface;
                created = true;
            }
            else
            {
                surface.SetMacros(macros);
            }

            _surfaceTargets[group.Key] = foreground;
            _surfaceRules[group.Key] = group.Rule;
            if (!surface.IsMoving)
            {
                surface.PlaceAt(foreground, SharedDevices.Current.Windows,
                    viewModel.Triggers.LayoutFor(group.Rule));
                if (created)
                {
                    surface.ShowAsPeer(this);
                }
            }
        }

        foreach (var key in _surfaces.Keys.Where(key => !active.Contains(key)).ToList())
        {
            RemoveSurface(key);
        }

        foreach (var gone in _hiddenInterfaceGroups
                     .Where(key => allGroups.All(group => group.Key != key)).ToList())
        {
            _hiddenInterfaceGroups.Remove(gone);
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
            var origin = SharedDevices.Current.Windows.ClientOrigin(target.Handle);
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
            RemoveSurface(key);
        }
    }

    private void RemoveSurface(string key)
    {
        if (_surfaces.Remove(key, out var surface))
        {
            surface.Close();
        }

        _surfaceTargets.Remove(key);
        _surfaceRules.Remove(key);
    }

    private static bool ForegroundRuleMatches(WindowRule rule, WindowInfo foreground)
    {
        try
        {
            return SharedDevices.Current.Windows.Find(rule.MatchValue, rule.Reading.Match,
                rule.Reading.Compare, rule.Options)?.Handle == foreground.Handle;
        }
        catch
        {
            return false;
        }
    }
}
