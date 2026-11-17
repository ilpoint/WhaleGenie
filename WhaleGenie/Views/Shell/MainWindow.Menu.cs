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
    // The floating macro menu: what it shows for the window in front, where its cells sit, and
    // what a press outside it means. It keeps its own open flags so the session and surface code
    // does not have to be read to change the menu.
    private MacroMenuWindow? _menu;

    private WindowInfo? _menuForeground;
    private int _menuOpen;
    private int _menuChildOpen;
    private int _menuPopupOpen;
    private int _menuSearchFocused;
    private MenuScreenBounds? _menuBounds;

    private sealed record MenuScreenBounds(int Left, int Top, int Right, int Bottom)
    {
        public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
    }

    private void OnMenuRequested()
        => Dispatcher.UIThread.Post(() => _ = OpenMacroMenuAsync());

    private async Task OpenMacroMenuAsync()
    {
        if (_menu is not null || DataContext is not MainViewModel viewModel)
        {
            _menu?.Activate();
            return;
        }

        WindowInfo? previous = null;
        try
        {
            previous = SharedDevices.Current.Windows.Foreground();
        }
        catch
        {
            // The menu can still run a macro that names its own window.
        }

        if (previous is not null
            && viewModel.Triggers.IsMenuBlocked(previous, SharedDevices.Current.Windows))
        {
            return;
        }

        var layout = viewModel.Triggers.BuildMenuLayout(previous,
            SharedDevices.Current.Windows, [.. viewModel.Macros]);
        HideSurface();
        MacroTriggerGate.Enter();
        MacroMenuWindow menu;
        try
        {
            var actions = new MenuWindowActions(
                macro =>
                {
                    RestoreMenuForeground();
                    OpenEditor(macro, macro, draft: false);
                },
                macro => Dispatcher.UIThread.Post(() => _ = ShowRunDebugAsync(macro)),
                () =>
                {
                    var foreground = _menuForeground;
                    Dispatcher.UIThread.Post(() => _ = OpenMenuSettingsAsync(foreground));
                },
                RestoreMenuForeground,
                () => AddCurrentWindowRuleAsync(viewModel),
                () => BlockCurrentWindowAsync(viewModel),
                () => AddCommonMenuPageAsync(viewModel),
                pageId => RemoveCommonMenuPageAsync(pageId, viewModel),
                pageId => AddContextMenuPageAsync(pageId, viewModel),
                pageId => RemoveContextMenuPageAsync(pageId, viewModel),
                slot => ChooseMenuMacroAsync(slot, viewModel),
                slot => ChooseMenuIconAsync(slot, viewModel),
                (source, target) => MoveMenuCellAsync(source, target, viewModel));
            menu = new MacroMenuWindow(layout, actions, MenuForegroundProcess(previous));
        }
        catch (Exception error)
        {
            MacroTriggerGate.Exit();
            AppDiagnostics.Record("menu.create", error);
            return;
        }
        _menu = menu;
        _menuForeground = previous;
        _menuChildOpen = 0;
        _menuPopupOpen = 0;
        _menuSearchFocused = 0;
        Volatile.Write(ref _menuOpen, 1);
        menu.SearchFocusChanged += focused =>
            Volatile.Write(ref _menuSearchFocused, focused ? 1 : 0);
        menu.PopupOpenChanged += open =>
            Volatile.Write(ref _menuPopupOpen, open ? 1 : 0);
        menu.PositionChanged += (_, _) => UpdateMenuBounds(menu);
        menu.SizeChanged += (_, _) => UpdateMenuBounds(menu);
        menu.Closed += (_, _) =>
        {
            MacroTriggerGate.Exit();
            Volatile.Write(ref _menuOpen, 0);
            Volatile.Write(ref _menuChildOpen, 0);
            Volatile.Write(ref _menuPopupOpen, 0);
            Volatile.Write(ref _menuSearchFocused, 0);
            Volatile.Write(ref _menuBounds, null);
            if (ReferenceEquals(_menu, menu))
            {
                _menu = null;
                _menuForeground = null;
            }

            Dispatcher.UIThread.Post(() => RefreshSurface(viewModel));
        };
        try
        {
            menu.ShowAsPeer(this);
            UpdateMenuBounds(menu);
        }
        catch (Exception error)
        {
            _menu = null;
            _menuForeground = null;
            Volatile.Write(ref _menuOpen, 0);
            MacroTriggerGate.Exit();
            AppDiagnostics.Record("menu.show", error);
            return;
        }

        var chosen = await menu.Answer;
        if (chosen is null)
        {
            return;
        }

        if (previous is not null)
        {
            try
            {
                SharedDevices.Current.Windows.Activate(previous.Handle);
                await Task.Delay(40);
            }
            catch
            {
                // Best effort: a macro that names its own target does not depend on this.
            }
        }

        _triggers?.RunOnce(chosen);
    }

    private MenuRuntimeLayout RebuildMenuLayout(MainViewModel viewModel)
    {
        WindowInfo? foreground = null;
        try
        {
            foreground = _menuForeground ?? SharedDevices.Current.Windows.Foreground();
        }
        catch
        {
            // The default context page is still valid when the foreground cannot be read.
        }

        return viewModel.Triggers.BuildMenuLayout(foreground,
            SharedDevices.Current.Windows, [.. viewModel.Macros]);
    }

    private void UpdateMenuBounds(MacroMenuWindow menu)
    {
        var left = menu.Position.X;
        var top = menu.Position.Y;
        var scale = menu.RenderScaling;
        var right = left + (int)Math.Ceiling(menu.Width * scale);
        var bottom = top + (int)Math.Ceiling(menu.Height * scale);
        Volatile.Write(ref _menuBounds, new MenuScreenBounds(left, top, right, bottom));
    }

    private void RestoreMenuForeground()
    {
        if (_menuForeground is not { } foreground)
        {
            return;
        }

        try
        {
            SharedDevices.Current.Windows.Activate(foreground.Handle);
        }
        catch
        {
            // The window can be gone; the menu still remains usable.
        }
    }

    private async Task<MenuRuntimeLayout?> ChooseMenuMacroAsync(MenuSlotRef slot,
        MainViewModel viewModel)
    {
        if (_menu is null)
        {
            return null;
        }

        MacroItem? macro;
        Interlocked.Increment(ref _menuChildOpen);
        try
        {
            macro = await MenuMacroPickerWindow.ShowAsync(_menu, [.. viewModel.Macros]);
        }
        finally
        {
            Interlocked.Decrement(ref _menuChildOpen);
        }
        if (macro is null)
        {
            return null;
        }

        if (slot.Cell is { } existing)
        {
            existing.Entry.MacroName = macro.Name;
        }
        else
        {
            viewModel.Triggers.Entries.Add(new TriggerEntry
            {
                Spec = new MenuSpec(slot.PageId, slot.Row, slot.Column),
                MacroName = macro.Name,
                IsEnabled = true,
            });
        }

        viewModel.MarkMenuChanged();
        return RebuildMenuLayout(viewModel);
    }

    private Task<MenuRuntimeLayout?> AddCurrentWindowRuleAsync(MainViewModel viewModel)
    {
        if (_menuForeground is not { } foreground
            || MenuForegroundProcess(foreground) is not { Length: > 0 } process)
        {
            return Task.FromResult<MenuRuntimeLayout?>(null);
        }

        string className;
        try
        {
            className = SharedDevices.Current.Windows.ClassOf(foreground.Handle);
        }
        catch
        {
            className = string.Empty;
        }
        viewModel.Triggers.EnsureProcessMenuRule(foreground, process, className);
        viewModel.MarkMenuChanged();
        return Task.FromResult<MenuRuntimeLayout?>(RebuildMenuLayout(viewModel));
    }

    private Task<bool> BlockCurrentWindowAsync(MainViewModel viewModel)
    {
        if (_menuForeground is not { } foreground
            || MenuForegroundProcess(foreground) is not { Length: > 0 } process)
        {
            return Task.FromResult(false);
        }

        string className;
        try
        {
            className = SharedDevices.Current.Windows.ClassOf(foreground.Handle);
        }
        catch
        {
            className = string.Empty;
        }
        if (viewModel.Triggers.BlockMenuForProcess(foreground, process, className))
        {
            viewModel.MarkMenuChanged();
        }

        return Task.FromResult(true);
    }

    private Task<MenuRuntimeLayout?> AddCommonMenuPageAsync(MainViewModel viewModel)
    {
        if (viewModel.Triggers.CommonMenuPages().Count < ProjectTriggers.MaxCommonMenuPages)
        {
            viewModel.Triggers.AddCommonMenuPage();
            viewModel.MarkMenuChanged();
        }
        return Task.FromResult<MenuRuntimeLayout?>(RebuildMenuLayout(viewModel));
    }

    private Task<MenuRuntimeLayout?> RemoveCommonMenuPageAsync(string pageId,
        MainViewModel viewModel)
    {
        if (viewModel.Triggers.CanRemoveMenuPage(pageId))
        {
            viewModel.Triggers.RemoveMenuPage(pageId);
            viewModel.MarkMenuChanged();
        }
        return Task.FromResult<MenuRuntimeLayout?>(RebuildMenuLayout(viewModel));
    }

    private Task<MenuRuntimeLayout?> AddContextMenuPageAsync(string pageId,
        MainViewModel viewModel)
    {
        var page = viewModel.Triggers.MenuPages.FirstOrDefault(candidate =>
            candidate.Id == pageId);
        if (page is null)
        {
            return Task.FromResult<MenuRuntimeLayout?>(RebuildMenuLayout(viewModel));
        }

        if (page.IsDefault)
        {
            if (viewModel.Triggers.DefaultContextMenuPages().Count
                < ProjectTriggers.MaxCommonMenuPages)
            {
                viewModel.Triggers.AddDefaultContextPage();
                viewModel.MarkMenuChanged();
            }
        }
        else if (viewModel.Triggers.ContextPagesFor(page.Window).Count
                 < ProjectTriggers.MaxCommonMenuPages)
        {
            viewModel.Triggers.AddContextMenuPage(page.Window);
            viewModel.MarkMenuChanged();
        }
        return Task.FromResult<MenuRuntimeLayout?>(RebuildMenuLayout(viewModel));
    }

    private Task<MenuRuntimeLayout?> RemoveContextMenuPageAsync(string pageId,
        MainViewModel viewModel)
    {
        if (viewModel.Triggers.CanRemoveMenuPage(pageId))
        {
            viewModel.Triggers.RemoveMenuPage(pageId);
            viewModel.MarkMenuChanged();
        }
        return Task.FromResult<MenuRuntimeLayout?>(RebuildMenuLayout(viewModel));
    }

    private async Task<MenuRuntimeLayout?> ChooseMenuIconAsync(MenuSlotRef slot,
        MainViewModel viewModel)
    {
        Interlocked.Increment(ref _menuChildOpen);
        try
        {
            if (slot.Cell is not { } cell || _menu is null
                || await MenuIconPickerWindow.ShowAsync(_menu) is not { } icon)
            {
                return null;
            }

            cell.Entry.SetMenuIcon(icon.IsEmpty
                ? null
                : icon.Kind == MenuIconKind.Image
                    ? new MenuIconReference(icon.Kind, CopyMenuIcon(icon.Value, viewModel))
                    : icon);

            viewModel.MarkMenuChanged();
            return RebuildMenuLayout(viewModel);
        }
        finally
        {
            Interlocked.Decrement(ref _menuChildOpen);
        }
    }

    private static string MenuForegroundProcess(WindowInfo? foreground)
    {
        if (foreground is null)
        {
            return string.Empty;
        }

        try
        {
            var process = SharedDevices.Current.Windows.ProcessOf(foreground.Handle).Trim();
            var own = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? string.Empty);
            return process.Length == 0
                   || string.Equals(process, own, StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : process;
        }
        catch
        {
            return string.Empty;
        }
    }

    private async Task<MenuRuntimeLayout?> MoveMenuCellAsync(MenuSlotRef source,
        MenuSlotRef target, MainViewModel viewModel)
    {
        if (source.Cell is not { } from)
        {
            return null;
        }

        if (target.Cell is { } to)
        {
            var sourceCell = from.Entry.Spec is MenuSpec menu ? menu : new MenuSpec();
            var targetCell = to.Entry.Spec is MenuSpec other ? other : new MenuSpec();
            from.Entry.MoveMenuCell(target.PageId, target.Row, target.Column);
            to.Entry.MoveMenuCell(source.PageId, sourceCell.Row, sourceCell.Column);
        }
        else
        {
            from.Entry.MoveMenuCell(target.PageId, target.Row, target.Column);
        }

        viewModel.MarkMenuChanged();
        return await Task.FromResult<MenuRuntimeLayout?>(RebuildMenuLayout(viewModel));
    }

    private static string CopyMenuIcon(string path, MainViewModel viewModel)
        => ImageAssets.Adopt(path, viewModel.CurrentPath);

    private void OnMenuMouseDown(MouseHookEvent e)
    {
        try
        {
            if (Volatile.Read(ref _menuOpen) == 0)
            {
                return;
            }

            if (e.Button is HookMouseButton.Middle
                or HookMouseButton.Back or HookMouseButton.Forward)
            {
                var name = KeyNames.MouseName(e.Button);
                var plan = Volatile.Read(ref _globalHotkeyPlan);
                if (plan.MenuLauncher.Length > 0
                    && TriggerKeys.Binds(plan.MenuLauncher, name, []))
                {
                    e.Suppress = true;
                    Dispatcher.UIThread.Post(() => _menu?.CloseFromLauncher());
                    return;
                }
            }

            if (CanCloseMenuFromOutside(
                    Volatile.Read(ref _menuChildOpen),
                    Volatile.Read(ref _menuPopupOpen))
                && Volatile.Read(ref _menuBounds) is { } bounds
                && GetCursorPosition() is { } point
                && !bounds.Contains(point.X, point.Y))
            {
                Dispatcher.UIThread.Post(() => _menu?.CloseFromOutside());
            }
        }
        catch (Exception error)
        {
            AppDiagnostics.Record("hook.menu-mouse", error);
        }
    }

    internal static bool CanCloseMenuFromOutside(int childOpen, int popupOpen)
        => childOpen == 0 && popupOpen == 0;
}
