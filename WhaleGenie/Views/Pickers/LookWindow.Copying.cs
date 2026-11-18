using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Assets;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;


namespace WhaleGenie.Views;

// Filling the list, copying rows out, and choosing which marks are shown.
public partial class LookWindow : AppWindow
{
    /// <summary>
    /// Replaces everything this window is showing with another look at the same step: the picture,
    /// the header, the marks and the writing that was read.
    /// </summary>
    private void Refill(StepLook look)
    {
        _look = look;
        _frame?.Dispose();
        _needle?.Dispose();
        _frame = look.Frame.IsEmpty ? null : BitmapImages.ToBitmap(look.Frame);
        _needle = look.Needle is { IsEmpty: false } needle ? BitmapImages.ToBitmap(needle) : null;

        FillHeader(look);
        ShowNeedle();
        Draw();
    }

    /// <summary>Puts what was read on the clipboard, which is where the person reading it wants it.</summary>
    /// <summary>
    /// Puts the checked rows' writing on the clipboard, one reading to a line. A row that found
    /// nothing still says what it is — where it looked, what it was after — so the list copied out
    /// of is the list that was read, not only the rows that happened to hold a label.
    /// </summary>
    private void OnCopyMarks(object? sender, RoutedEventArgs e) => CopyRows(null);

    private void OnCopyRow(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: int index })
        {
            CopyRows(index);
        }
    }

    private void CopyRows(int? only)
    {
        if (_devices?.Clipboard is not { } clipboard)
        {
            return;
        }

        var lines = new List<string>();
        for (var index = 0; index < _markTexts.Count; index++)
        {
            if (only is { } wanted && wanted != index)
            {
                continue;
            }

            if (!_markRows[index].IsVisible || (only is null && _markToggles[index].IsChecked != true))
            {
                continue;
            }

            // The row that stands for the search area is left out: what is being copied is what the
            // step read, not where it looked.
            if (_markRoles[index] == Strings.Get("Look.Role.Area"))
            {
                continue;
            }

            if (_markTexts[index].Length > 0)
            {
                lines.Add(_markTexts[index]);
            }
        }

        if (lines.Count == 0)
        {
            return;
        }

        clipboard.WriteText(string.Join(Environment.NewLine, lines));
        SetStatus(Strings.Get("Look.TextCopied"));
    }

    /// <summary>
    /// Checks every row, which is what a person wants before copying the lot: the rows are the
    /// marks drawn on the picture, and a list where some are already put away is a list that copies
    /// part of itself without saying so.
    /// </summary>
    private void OnSelectAllMarks(object? sender, RoutedEventArgs e)
    {
        foreach (var toggle in _markToggles)
        {
            toggle.IsChecked = true;
        }
    }

    /// <summary>
    /// Turns every row the other way: what was ticked is left out, what was left out is ticked. The
    /// rows a filter narrowed away are not touched — the list on show is what "all of them" means
    /// while a filter is on, and turning rows nobody can see is a change with nothing to show for it.
    /// </summary>
    private void OnInvertMarks(object? sender, RoutedEventArgs e)
    {
        foreach (var item in _markToggles
                     .Select((toggle, index) => (Toggle: toggle, index))
                     .Where(item => _markRows[item.index].IsVisible))
        {
            item.Toggle.IsChecked = item.Toggle.IsChecked != true;
        }
    }

    /// <summary>Narrows the list to the rows whose writing or role mentions what was typed.</summary>
    private void OnMarkFilterChanged(object? sender, TextChangedEventArgs e) => ApplyMarkFilter();

    private void ApplyMarkFilter()
    {
        var wanted = this.FindControl<TextBox>("MarkFilter")?.Text?.Trim() ?? string.Empty;
        for (var index = 0; index < _markRows.Count; index++)
        {
            _markRows[index].IsVisible = wanted.Length == 0
                || _markTexts[index].Contains(wanted, StringComparison.OrdinalIgnoreCase)
                || _markRoles[index].Contains(wanted, StringComparison.OrdinalIgnoreCase);
        }
    }
}
