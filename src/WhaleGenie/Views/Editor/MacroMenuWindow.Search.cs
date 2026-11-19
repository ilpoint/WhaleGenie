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

// The search box: matching, focusing, and the keys it takes while it is open.
public sealed partial class MacroMenuWindow
{
    private void OnSearchChanged()
    {
        var wanted = _search.Text?.Trim() ?? string.Empty;
        _selected = null;
        if (wanted.Length > 0)
        {
            for (var index = 0; index < _layout.CommonPages.Count; index++)
            {
                if (_layout.CommonPages[index].Cells.Any(MatchesSearch))
                {
                    _commonPage = index;
                    break;
                }
            }
        }
        RenderLayout();
    }

    private bool MatchesSearch(MenuRuntimeCell cell)
    {
        var wanted = _search.Text?.Trim() ?? string.Empty;
        return wanted.Length == 0
               || cell.Macro.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase)
               || cell.Macro.Description.Contains(wanted, StringComparison.OrdinalIgnoreCase);
    }

    private void BeginSearchFocus()
    {
        if (_closing || Volatile.Read(ref _searchFocused) == 1)
        {
            return;
        }

        Volatile.Write(ref _searchFocused, 1);
        SearchFocusChanged?.Invoke(true);
        ShowActivated = true;
        SetNoActivate(false);
        Activate();
    }

    private void EndSearchFocus()
    {
        if (_closing || Interlocked.Exchange(ref _searchFocused, 0) == 0)
        {
            return;
        }

        var restore = IsActive;
        ShowActivated = false;
        ApplyNoActivate();
        SearchFocusChanged?.Invoke(false);
        if (restore && !_menuDragging && !_pinned)
        {
            Dispatcher.UIThread.Post(_actions.RestoreForeground);
        }
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down or Key.Up:
                if (_selected is null)
                {
                    _selected = FirstVisibleMatch();
                }
                else
                {
                    MoveSelection(e.Key == Key.Down ? 1 : -1, 0);
                }
                e.Handled = true;
                break;
            case Key.Enter:
                _selected ??= FirstVisibleMatch();
                if (_selected is { } selected)
                {
                    _ = ActivateSlotAsync(selected);
                }
                e.Handled = true;
                break;
            case Key.Escape:
                HandleKey("Esc");
                e.Handled = true;
                break;
        }
    }

    private MenuSlotRef? FirstVisibleMatch()
    {
        var common = _layout.CommonPages[_commonPage];
        var context = _layout.ContextPages[_contextPage];
        var cell = common.Cells.Concat(context.Cells)
            .Where(MatchesSearch)
            .OrderBy(item => item.Row)
            .ThenBy(item => item.Column)
            .FirstOrDefault();
        if (cell is null)
        {
            return null;
        }

        return new MenuSlotRef(
            common.Cells.Contains(cell) ? common.PageId : context.PageId,
            cell.Row, cell.Column, cell);
    }
}
