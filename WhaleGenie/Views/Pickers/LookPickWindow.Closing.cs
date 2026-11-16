using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;


namespace WhaleGenie.Views;

// The last steps: moving a colour, saying what happened, confirming, and the keys that close it.
public partial class LookPickWindow : AppWindow
{
    /// <summary>
    /// Moves the picked line to where the picture was clicked: the same arithmetic as adding one,
    /// only it replaces a line instead of appending one.
    /// </summary>
    private void MoveColour(int index, ScreenPoint point, ScreenPoint local)
    {
        var offset = OffsetAt(point);
        var colour = _frame[local.X, local.Y].ToHex();

        _added[index] = ColourPointLine.WithCore(_added[index],
            offset.X.ToString(CultureInfo.InvariantCulture),
            offset.Y.ToString(CultureInfo.InvariantCulture), colour);
        _addedAt[index] = point;
        _picked = point;
        RebuildRows();
        Draw();
        Say(Strings.Format("LookPick.Moved", offset.X, offset.Y));
    }

    /// <summary>How far a place is from the middle of what the step found, which is what it clicks by.</summary>
    internal ScreenPoint OffsetAt(ScreenPoint point)
        => new(point.X - _centre.X, point.Y - _centre.Y);

    private void Say(string text)
    {
        if (_readout is not null)
        {
            _readout.Text = text;
        }
    }

    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        if (_mode == LookPickMode.Offset)
        {
            if (_picked is { } point)
            {
                Close(OffsetAt(point));
            }

            return;
        }

        if (_added.Count > 0)
        {
            Close(_added.ToArray());
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                Close(null);
                break;

            case Key.Enter:
                e.Handled = true;
                OnConfirm(sender, new RoutedEventArgs());
                break;

            default:
                break;
        }
    }
}
