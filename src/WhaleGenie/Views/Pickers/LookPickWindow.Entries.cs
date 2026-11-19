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

// Editing an entry as text: what it starts as, what it accepts, and how it is put back.
public partial class LookPickWindow : AppWindow
{
    /// <summary>
    /// Changes one line by typing it. The marker follows the numbers when they still name a place:
    /// editing "12" into "20" is asking for the point twenty pixels along, not for a line whose
    /// marker stayed behind.
    /// </summary>
    private async void OnEditEntry(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: int index } || index < 0 || index >= _added.Count)
        {
            return;
        }

        if (await PickEntryWindow.EditAsync(this, _added[index], _variables) is { } edited)
        {
            ApplyEntry(index, edited);
        }
    }

    /// <summary>Adds one line by typing it, for a point that is easier to state than to aim at.</summary>
    private async void OnAddEntry(object? sender, RoutedEventArgs e)
    {
        if (await PickEntryWindow.AddAsync(this, StartingLine(), _variables) is { } line)
        {
            AppendEntry(line);
        }
    }

    /// <summary>
    /// What a new line starts as: the point the offsets are counted from, with the colour that is
    /// there. Starting from nothing would leave two numbers and a colour to be filled in from memory.
    /// </summary>
    private string StartingLine()
    {
        var local = new ScreenPoint(_centre.X - _origin.X, _centre.Y - _origin.Y);
        var colour = local.X >= 0 && local.Y >= 0
            && local.X < _frame.Width && local.Y < _frame.Height
            ? _frame[local.X, local.Y].ToHex()
            : "#000000";
        return string.Create(CultureInfo.InvariantCulture, $"0,0,{colour}");
    }

    /// <summary>
    /// Adds a line that was typed. It goes at the end, where a click would have put it, and a place
    /// already in the list is not written twice.
    /// </summary>
    internal void AppendEntry(string edited)
    {
        if (!ColourPointLine.TryParse(edited, out var line))
        {
            return;
        }

        if (Contains(line))
        {
            Say(Strings.Get("LookPick.ColoursRepeat"));
            return;
        }

        _added.Add(line);
        _addedAt.Add(Offsets(line, out var dx, out var dy)
            ? new ScreenPoint(_centre.X + dx, _centre.Y + dy)
            : _centre);
        _selected = -1;
        _picked = null;
        RebuildRows();
        Draw();
        Say(Strings.Format("LookPick.ColoursAdded", line, _added.Count));
        UpdateConfirm();
    }

    /// <summary>
    /// Whether the list already holds this place, spaces and letter case aside. Only the offset and
    /// the colours are compared: whether a place is strict, or allows a tolerance of its own, does
    /// not make it another place.
    /// </summary>
    private bool Contains(string entry)
        => _added.Any(kept => string.Equals(
            Squeeze(ColourPointLine.Core(kept)),
            Squeeze(ColourPointLine.Core(entry)),
            StringComparison.OrdinalIgnoreCase));

    private static string Squeeze(string line) => line.Replace(" ", string.Empty, StringComparison.Ordinal);

    /// <summary>
    /// The two numbers at the front of a line, when they are numbers: a line that names its offset by
    /// a formula has no place on the picture to draw until the macro runs, and stays at the middle.
    /// </summary>
    private static bool Offsets(string line, out int dx, out int dy)
    {
        dx = 0;
        dy = 0;
        var parts = ColourPointText.Split(line);
        return parts.Count >= 2
            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out dx)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out dy);
    }

    /// <summary>Puts a line that was typed in place of one that was clicked.</summary>
    internal void ApplyEntry(int index, string edited)
    {
        if (index < 0 || index >= _added.Count
            || !ColourPointLine.TryParse(edited, out var line))
        {
            return;
        }

        _added[index] = line;
        if (Offsets(line, out var dx, out var dy))
        {
            _addedAt[index] = new ScreenPoint(_centre.X + dx, _centre.Y + dy);
        }

        RebuildRows();
        Draw();
        Say(Strings.Format("LookPick.ColoursEdited", line));
    }

    private void OnRemoveEntry(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: int index })
        {
            RemoveEntry(index);
        }
    }

    /// <summary>Takes one line out of the list, and its marker with it.</summary>
    internal void RemoveEntry(int index)
    {
        if (index < 0 || index >= _added.Count)
        {
            return;
        }

        _added.RemoveAt(index);
        _addedAt.RemoveAt(index);
        if (_selected == index)
        {
            _selected = -1;
            _picked = null;
        }
        else if (_selected > index)
        {
            _selected--;
        }

        RebuildRows();
        Draw();
        UpdateConfirm();
        Say(Strings.Format("LookPick.ColoursLeft", _added.Count));
    }
}
