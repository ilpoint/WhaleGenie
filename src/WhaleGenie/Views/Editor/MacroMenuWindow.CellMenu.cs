using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Lucide.Avalonia;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;

using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

// A cell's own menu: its answers, replacing a macro or an icon, and the popup around it.
public sealed partial class MacroMenuWindow
{
    private ContextMenu CellMenu(MenuSlotRef slot)
    {
        var menu = new ContextMenu();
        foreach (var (label, action) in CellAnswers(slot))
        {
            menu.Items.Add(Item(label, action));
        }

        return menu;
    }

    /// <summary>
    /// The four things a cell answers with, written once and used twice: as the menu's items, and as
    /// the rows of the panel that stands in for the menu when the popup it is does not appear.
    /// </summary>
    private List<(string Label, Action Action)> CellAnswers(MenuSlotRef slot)
        =>
        [
            (Strings.Get("Menu.EditMacro"), () =>
            {
                CloseForAction();
                _actions.OpenMacroEditor(slot.Cell!.Macro);
            }),
            (Strings.Get("Menu.Replace"), () => _ = ReplaceAsync(slot)),
            (Strings.Get("Menu.ChooseIcon"), () => _ = ReiconAsync(slot)),
            (Strings.Get("Menu.Debug"), () =>
            {
                CloseForAction();
                _actions.DebugMacro(slot.Cell!.Macro);
            }),
        ];

    private async Task ReplaceAsync(MenuSlotRef slot)
    {
        if (await _actions.ChooseMacro(slot) is { } updated)
        {
            SetLayout(updated);
        }
    }

    private async Task ReiconAsync(MenuSlotRef slot)
    {
        if (await _actions.ChooseIcon(slot) is { } updated)
        {
            SetLayout(updated);
        }
    }

    private void AttachContextMenu(Control control, ContextMenu menu,
        Func<List<(string Label, Action Action)>> answers)
    {
        control.ContextMenu = menu;

        // Whoever opens the menu — the framework on its own or the release below — the window has
        // to be allowed to activate for the popup to be presented at all, and the open has to be
        // watched. Doing it here covers both ways in.
        menu.Opening += (_, _) => PreparePopup(control, menu, answers);
        menu.Opened += (_, _) => PopupOpened();
        menu.Closed += (_, _) =>
        {
            PopupClosed();
            StopWatching();

            // A context menu is a separate top-level popup. The menu window has to be allowed to
            // activate for that popup to be presented at all; once it is gone, put the no-activate
            // style back so the menu does not start taking focus from the program underneath it.
            if (!IsSearchFocused)
            {
                ShowActivated = false;
                ApplyNoActivate();
            }
        };
        control.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Right || menu.IsOpen || _closing)
            {
                return;
            }

            e.Handled = true;
            OpenCellMenu(control, menu, answers);
        };
    }

    /// <summary>
    /// Gets the window ready for a popup and starts watching whether it arrives.
    /// </summary>
    /// <remarks>
    /// WS_EX_NOACTIVATE keeps the menu from stealing focus while it is only being looked at. A popup
    /// cannot be shown inside that style, so this is the same temporary trade the search box makes
    /// while it needs the keyboard. A menu that reports itself open while showing nothing on some
    /// machines is what the watch is for: nothing else about the palette can tell those two apart.
    /// </remarks>
    private void PreparePopup(Control control, ContextMenu menu,
        Func<List<(string Label, Action Action)>> answers)
    {
        ShowActivated = true;
        SetNoActivate(false);
        Activate();

        AppDiagnostics.Note("menu.request", $"cell:{control.Name ?? control.GetType().Name}");

        StopWatching();

        var watch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        watch.Tick += (_, _) =>
        {
            watch.Stop();
            if (!ReferenceEquals(_menuWatch, watch))
            {
                return;
            }

            _menuWatch = null;
            if (menu.IsOpen)
            {
                AppDiagnostics.Note("menu.opened", $"cell:{control.Name ?? control.GetType().Name}");
                return;
            }

            AppDiagnostics.Note("menu.notopened", $"cell:{control.Name ?? control.GetType().Name}");
        };

        _menuWatch = watch;
        watch.Start();
    }

    private void StopWatching()
    {
        if (_menuWatch is { } watch)
        {
            watch.Stop();
            _menuWatch = null;
        }
    }

    /// <summary>
    /// Opens a cell's menu at the pointer when the framework has not already done so.
    /// </summary>
    /// <remarks>
    /// The release rather than the press: a menu opened while the press is still being routed is
    /// closed again by the release that follows it, which reads as a menu that never appeared.
    /// </remarks>
    private void OpenCellMenu(Control control, ContextMenu menu,
        Func<List<(string Label, Action Action)>> answers)
    {
        if (_closing || menu.IsOpen)
        {
            return;
        }

        try
        {
            menu.Open(control);
        }
        catch (Exception error)
        {
            AppDiagnostics.Record("menu.cell.open", error);
            StopWatching();
        }
    }

    private void PopupOpened()
    {
        if (Interlocked.Increment(ref _openPopupCount) == 1)
        {
            PopupOpenChanged?.Invoke(true);
        }
    }

    private void PopupClosed()
    {
        if (Interlocked.Decrement(ref _openPopupCount) <= 0)
        {
            Interlocked.Exchange(ref _openPopupCount, 0);
            PopupOpenChanged?.Invoke(false);
        }
    }

    private static MenuItem Item(string label, Action action)
    {
        var item = new MenuItem { Header = label };
        item.Click += (_, _) => action();
        return item;
    }
}
