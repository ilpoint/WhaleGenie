using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using WhaleGenie.Core.Devices;
using WhaleGenie.Models;
using WhaleGenie.Storage;
using WhaleGenie.Views;

namespace WhaleGenie.Runtime;

/// <summary>
/// The things a native surface needs from the window that owns it but must not learn about: opening
/// the editor, the run window and the settings, and hiding or refreshing the panels that live beside
/// the menu. A check can hand in a stand-in instead of a window.
/// </summary>
public interface INativeSurfaceActions
{
    void OpenEditor(MacroItem macro);

    void OpenRun(MacroItem macro);

    void OpenSettings(WindowInfo? foreground);

    void HidePanels();

    void RefreshPanels();
}

/// <summary>
/// The floating work that belongs beside a window rather than inside it: the macro menu, the wheel
/// and the interface panels. It reads the project and the device layer directly, so it keeps working
/// when the web shell is the visible window and the old main window is not.
/// </summary>
internal sealed partial class NativeSurfaceHost : IDisposable
{
    private readonly ProjectWorkspace _workspace;

    private readonly TriggerRuntime _runtime;

    private readonly IDeviceLayer _devices;

    private readonly Func<Window?> _owner;

    private readonly Func<GlobalHotkeyPlan> _plan;

    private readonly INativeSurfaceActions _actions;

    private MacroMenuWindow? _menu;

    private WindowInfo? _menuForeground;
    private int _menuOpen;
    private int _menuChildOpen;
    private int _menuPopupOpen;
    private int _menuSearchFocused;
    private int _menuPinned;
    private MenuScreenBounds? _menuBounds;

    public NativeSurfaceHost(ProjectWorkspace workspace, TriggerRuntime runtime, IDeviceLayer devices,
        Func<Window?> owner, Func<GlobalHotkeyPlan> plan, INativeSurfaceActions actions)
    {
        _workspace = workspace;
        _runtime = runtime;
        _devices = devices;
        _owner = owner;
        _plan = plan;
        _actions = actions;
    }

    public bool MenuOpen => Volatile.Read(ref _menuOpen) == 1;

    public bool MenuSearchFocused => Volatile.Read(ref _menuSearchFocused) == 1;

    public bool MenuPinned => Volatile.Read(ref _menuPinned) == 1;

    /// <summary>Opens the menu; the hook may call this through the trigger host's surface.</summary>
    public void OpenMenu() => Dispatcher.UIThread.Post(() => _ = OpenMacroMenuAsync());

    public void CloseMenuFromLauncher() => _menu?.CloseFromLauncher();

    public void MenuHandleKey(string name) => _menu?.HandleKey(name);

    public void MenuMouseDown(MouseHookEvent e)
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
                var plan = _plan();
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

    /// <summary>
    /// The foreground moved while the menu was open. A pinned menu stays; a search box keeps its
    /// own close rule; otherwise a press elsewhere is the same as closing it.
    /// </summary>
    public void CloseMenuForForegroundChange()
    {
        if (_menu is not { IsPinned: false } menu
            || _menuForeground is not { } expected
            || !CanCloseMenuFromOutside(
                Volatile.Read(ref _menuChildOpen),
                Volatile.Read(ref _menuPopupOpen)))
        {
            return;
        }

        try
        {
            if (Foreground() is { } current
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

    private async Task OpenMacroMenuAsync()
    {
        if (_menu is not null)
        {
            _menu.Activate();
            return;
        }

        WindowInfo? previous = null;
        try
        {
            previous = _devices.Windows.Foreground();
        }
        catch
        {
            // The menu can still run a macro that names its own window.
        }

        if (previous is not null && _workspace.Triggers.IsMenuBlocked(previous, _devices.Windows))
        {
            return;
        }

        var layout = _workspace.Triggers.BuildMenuLayout(previous, _devices.Windows, [.. _workspace.Macros]);
        _actions.HidePanels();
        MacroTriggerGate.Enter();
        MacroMenuWindow menu;
        try
        {
            var actions = new MenuWindowActions(
                macro =>
                {
                    RestoreMenuForeground();
                    _actions.OpenEditor(macro);
                },
                macro => Dispatcher.UIThread.Post(() => _actions.OpenRun(macro)),
                () =>
                {
                    var foreground = _menuForeground;
                    Dispatcher.UIThread.Post(() => _actions.OpenSettings(foreground));
                },
                RestoreMenuForeground,
                () => AddCurrentWindowRuleAsync(),
                () => BlockCurrentWindowAsync(),
                () => AddCommonMenuPageAsync(),
                pageId => RemoveCommonMenuPageAsync(pageId),
                pageId => AddContextMenuPageAsync(pageId),
                pageId => RemoveContextMenuPageAsync(pageId),
                slot => ChooseMenuMacroAsync(slot),
                slot => ChooseMenuIconAsync(slot),
                (source, target) => MoveMenuCellAsync(source, target));
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
        _menuPinned = 0;
        Volatile.Write(ref _menuOpen, 1);
        menu.SearchFocusChanged += focused => Volatile.Write(ref _menuSearchFocused, focused ? 1 : 0);
        menu.PopupOpenChanged += open => Volatile.Write(ref _menuPopupOpen, open ? 1 : 0);
        menu.PinnedChanged += pinned => Volatile.Write(ref _menuPinned, pinned ? 1 : 0);
        menu.PositionChanged += (_, _) => UpdateMenuBounds(menu);
        menu.SizeChanged += (_, _) => UpdateMenuBounds(menu);
        menu.Closed += (_, _) =>
        {
            MacroTriggerGate.Exit();
            Volatile.Write(ref _menuOpen, 0);
            Volatile.Write(ref _menuChildOpen, 0);
            Volatile.Write(ref _menuPopupOpen, 0);
            Volatile.Write(ref _menuSearchFocused, 0);
            Volatile.Write(ref _menuPinned, 0);
            Volatile.Write(ref _menuBounds, null);
            if (ReferenceEquals(_menu, menu))
            {
                _menu = null;
                _menuForeground = null;
            }

            Dispatcher.UIThread.Post(_actions.RefreshPanels);
        };

        try
        {
            if (_owner() is { } owner)
            {
                menu.ShowAsPeer(owner);
            }
            else
            {
                menu.Show();
            }

            UpdateMenuBounds(menu);
        }
        catch (Exception error)
        {
            _menu = null;
            _menuForeground = null;
            Volatile.Write(ref _menuOpen, 0);
            Volatile.Write(ref _menuPinned, 0);
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
                _devices.Windows.Activate(previous.Handle);
                await Task.Delay(40);
            }
            catch
            {
                // Best effort: a macro that names its own target does not depend on this.
            }
        }

        _runtime.Triggers.RunOnce(chosen);
    }

    private MenuRuntimeLayout RebuildMenuLayout()
    {
        WindowInfo? foreground = null;
        try
        {
            foreground = _menuForeground ?? Foreground();
        }
        catch
        {
            // The default context page is still valid when the foreground cannot be read.
        }

        return _workspace.Triggers.BuildMenuLayout(foreground, _devices.Windows, [.. _workspace.Macros]);
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
            _devices.Windows.Activate(foreground.Handle);
        }
        catch
        {
            // The window can be gone; the menu still remains usable.
        }
    }

    private async Task<MenuRuntimeLayout?> ChooseMenuMacroAsync(MenuSlotRef slot)
    {
        if (_menu is null)
        {
            return null;
        }

        MacroItem? macro;
        Interlocked.Increment(ref _menuChildOpen);
        try
        {
            macro = await MenuMacroPickerWindow.ShowAsync(_menu, [.. _workspace.Macros]);
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
            _workspace.Triggers.Entries.Add(new TriggerEntry
            {
                Spec = new MenuSpec(slot.PageId, slot.Row, slot.Column),
                MacroName = macro.Name,
                IsEnabled = true,
            });
        }

        _workspace.MarkMenuChanged();
        return RebuildMenuLayout();
    }

    private Task<MenuRuntimeLayout?> AddCurrentWindowRuleAsync()
    {
        if (_menuForeground is not { } foreground
            || MenuForegroundProcess(foreground) is not { Length: > 0 } process)
        {
            return Task.FromResult<MenuRuntimeLayout?>(null);
        }

        var className = ClassOf(foreground);
        _workspace.Triggers.EnsureProcessMenuRule(foreground, process, className);
        _workspace.MarkMenuChanged();
        return Task.FromResult<MenuRuntimeLayout?>(RebuildMenuLayout());
    }

    private Task<bool> BlockCurrentWindowAsync()
    {
        if (_menuForeground is not { } foreground
            || MenuForegroundProcess(foreground) is not { Length: > 0 } process)
        {
            return Task.FromResult(false);
        }

        if (_workspace.Triggers.BlockMenuForProcess(foreground, process, ClassOf(foreground)))
        {
            _workspace.MarkMenuChanged();
        }

        return Task.FromResult(true);
    }

    private Task<MenuRuntimeLayout?> AddCommonMenuPageAsync()
    {
        if (_workspace.Triggers.CommonMenuPages().Count < ProjectTriggers.MaxCommonMenuPages)
        {
            _workspace.Triggers.AddCommonMenuPage();
            _workspace.MarkMenuChanged();
        }

        return Task.FromResult<MenuRuntimeLayout?>(RebuildMenuLayout());
    }

    private Task<MenuRuntimeLayout?> RemoveCommonMenuPageAsync(string pageId)
    {
        if (_workspace.Triggers.CanRemoveMenuPage(pageId))
        {
            _workspace.Triggers.RemoveMenuPage(pageId);
            _workspace.MarkMenuChanged();
        }

        return Task.FromResult<MenuRuntimeLayout?>(RebuildMenuLayout());
    }

    private Task<MenuRuntimeLayout?> AddContextMenuPageAsync(string pageId)
    {
        var page = _workspace.Triggers.MenuPages.FirstOrDefault(candidate => candidate.Id == pageId);
        if (page is null)
        {
            return Task.FromResult<MenuRuntimeLayout?>(RebuildMenuLayout());
        }

        if (page.IsDefault)
        {
            if (_workspace.Triggers.DefaultContextMenuPages().Count < ProjectTriggers.MaxCommonMenuPages)
            {
                _workspace.Triggers.AddDefaultContextPage();
                _workspace.MarkMenuChanged();
            }
        }
        else if (_workspace.Triggers.ContextPagesFor(page.Window).Count < ProjectTriggers.MaxCommonMenuPages)
        {
            _workspace.Triggers.AddContextMenuPage(page.Window);
            _workspace.MarkMenuChanged();
        }

        return Task.FromResult<MenuRuntimeLayout?>(RebuildMenuLayout());
    }

    private Task<MenuRuntimeLayout?> RemoveContextMenuPageAsync(string pageId)
    {
        if (_workspace.Triggers.CanRemoveMenuPage(pageId))
        {
            _workspace.Triggers.RemoveMenuPage(pageId);
            _workspace.MarkMenuChanged();
        }

        return Task.FromResult<MenuRuntimeLayout?>(RebuildMenuLayout());
    }

    private async Task<MenuRuntimeLayout?> ChooseMenuIconAsync(MenuSlotRef slot)
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
                    ? new MenuIconReference(icon.Kind, ImageAssets.Adopt(icon.Value, _workspace.CurrentPath))
                    : icon);

            _workspace.MarkMenuChanged();
            return RebuildMenuLayout();
        }
        finally
        {
            Interlocked.Decrement(ref _menuChildOpen);
        }
    }

    private string MenuForegroundProcess(WindowInfo? foreground)
    {
        if (foreground is null)
        {
            return string.Empty;
        }

        try
        {
            var process = _devices.Windows.ProcessOf(foreground.Handle).Trim();
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

    private string ClassOf(WindowInfo foreground)
    {
        try
        {
            return _devices.Windows.ClassOf(foreground.Handle);
        }
        catch
        {
            return string.Empty;
        }
    }

    private async Task<MenuRuntimeLayout?> MoveMenuCellAsync(MenuSlotRef source, MenuSlotRef target)
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

        _workspace.MarkMenuChanged();
        return await Task.FromResult<MenuRuntimeLayout?>(RebuildMenuLayout());
    }

    private WindowInfo? Foreground()
    {
        try
        {
            return _devices.Windows.Foreground();
        }
        catch
        {
            return null;
        }
    }

    private static NativePoint? GetCursorPosition()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        return GetCursorPos(out var point) ? point : null;
    }

    internal static bool CanCloseMenuFromOutside(int childOpen, int popupOpen)
        => childOpen == 0 && popupOpen == 0;

    public void Dispose()
    {
        if (_menu is { } menu)
        {
            _menu = null;
            _menuForeground = null;
            Volatile.Write(ref _menuOpen, 0);
            MacroTriggerGate.Exit();
            menu.Close();
        }
    }

    private sealed record MenuScreenBounds(int Left, int Top, int Right, int Bottom)
    {
        public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
