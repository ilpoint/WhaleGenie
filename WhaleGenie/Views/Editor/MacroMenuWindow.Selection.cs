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

// Moving the highlight by keys, and the dots that say which page is shown.
public sealed partial class MacroMenuWindow
{
    private void MoveSelection(int rowStep, int columnStep)
    {
        var common = _layout.CommonPages[_commonPage];
        var context = _layout.ContextPages[_contextPage];
        var commonCells = common.Cells.Where(MatchesSearch).ToList();
        var contextCells = context.Cells.Where(MatchesSearch).ToList();
        var page = _selected?.PageId == context.PageId
            || commonCells.Count == 0
            ? context
            : common;
        var cells = page.PageId == context.PageId ? contextCells : commonCells;
        if (cells.Count == 0)
        {
            return;
        }

        if (_selected is null)
        {
            var first = cells[0];
            _selected = new MenuSlotRef(page.PageId, first.Row, first.Column, first);
            RenderLayout();
            return;
        }

        var current = _selected.Cell is { } selected && cells.Contains(selected)
            ? selected
            : cells[0];
        var row = current.Row + rowStep;
        var column = current.Column + columnStep;
        while (row >= 0 && row < RegionRows && column >= 0 && column < Columns)
        {
            if (cells.FirstOrDefault(cell => cell.Row == row && cell.Column == column)
                is { } next)
            {
                _selected = new MenuSlotRef(page.PageId, next.Row, next.Column, next);
                RenderLayout();
                return;
            }

            row += rowStep;
            column += columnStep;
        }
    }

    private void RenderDots()
    {
        FillDots(_commonDots, _layout.CommonPages.Count, _commonPage, common: true);
        FillDots(_contextDots, _layout.ContextPages.Count, _contextPage, common: false);
    }

    private void FillDots(StackPanel target, int count, int current, bool common)
    {
        target.Children.Clear();
        for (var index = 0; index < count; index++)
        {
            var button = new Button
            {
                Name = $"{(common ? "Common" : "Context")}PageButton{index + 1}",
                Width = 12,
                Height = 16,
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = ButtonCursor,
                Content = new Ellipse
                {
                    Width = index == current ? 6 : 4,
                    Height = index == current ? 6 : 4,
                    Fill = index == current
                        ? ThemeBrush.Of("AccentFill")
                        : ThemeBrush.Of("TextTertiary"),
                },
            };
            var page = index;
            button.Click += (_, _) =>
            {
                if (common)
                {
                    _commonPage = page;
                }
                else
                {
                    _contextPage = page;
                }
                _selected = null;
                RenderLayout();
            };
            if (index > 0 && count > 1)
            {
                var remove = new MenuItem { Header = Strings.Get("Menu.RemovePage") };
                remove.Click += (_, _) => _ = RemovePageAsync(common, page);
                AttachContextMenu(button, new ContextMenu { Items = { remove } },
                    () =>
                    [
                        (Strings.Get("Menu.RemovePage"), () => _ = RemovePageAsync(common, page)),
                    ]);
            }
            target.Children.Add(button);
        }

        if (count < ProjectTriggers.MaxCommonMenuPages)
        {
            var add = DividerButton(AppIcons.Plus, Strings.Get("Menu.AddPage"));
            add.Name = common ? "AddCommonPageButton" : "AddContextPageButton";
            add.Click += async (_, _) =>
            {
                var added = common
                    ? await _actions.AddCommonPage()
                    : await _actions.AddContextPage(_layout.ContextPages[_contextPage].PageId);
                if (added is { } updated)
                {
                    SetLayout(updated);
                    if (common)
                    {
                        _commonPage = updated.CommonPages.Count - 1;
                    }
                    else
                    {
                        _contextPage = updated.ContextPages.Count - 1;
                    }
                    RenderLayout();
                }
            };
            target.Children.Add(add);
        }
    }
}
