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

// Adding a colour to the list, and the alternatives a search allows.
public partial class LookPickWindow : AppWindow
{
    /// <summary>
    /// A place on the picture, in the picture's own pixels, taken as the answer: one place in one
    /// mode, one more colour of the pattern in the other.
    /// </summary>
    internal void Pick(ScreenPoint local)
    {
        var point = PointAt(local);

        if (_mode == LookPickMode.Offset)
        {
            _picked = point;
            Draw();

            var offset = OffsetAt(point);
            Say(Strings.Format("LookPick.OffsetAt", offset.X, offset.Y));
            if (_confirm is not null)
            {
                _confirm.IsEnabled = true;
            }

            return;
        }

        if (_selected >= 0 && _selected < _added.Count)
        {
            MoveColour(_selected, point, local);
            return;
        }

        AddColour(point, local);
    }

    /// <summary>Where a place on the picture sits on the screen.</summary>
    internal ScreenPoint PointAt(ScreenPoint local)
        => new(_origin.X + local.X, _origin.Y + local.Y);

    /// <summary>
    /// Adds the offset and the colour at one place, once. The colour is read out of the picture the
    /// step was given rather than off the screen now: what is being described is the pattern as it
    /// was when the step looked, and the screen has moved on since.
    /// </summary>
    private void AddColour(ScreenPoint point, ScreenPoint local)
    {
        var offset = OffsetAt(point);
        var colour = _frame[local.X, local.Y].ToHex();
        var entry = string.Create(CultureInfo.InvariantCulture, $"{offset.X},{offset.Y},{colour}");

        if (Contains(entry))
        {
            Say(Strings.Get("LookPick.ColoursRepeat"));
            return;
        }

        _added.Add(entry);
        _addedAt.Add(point);
        _picked = null;

        // Adding leaves nothing selected: a click after this one is another colour, which is what
        // "one click, one colour" means. Moving is asked for by picking a line first.
        _selected = -1;
        Draw();
        RebuildRows();

        Say(Strings.Format("LookPick.ColoursAdded", entry, _added.Count));
        UpdateConfirm();
    }

    /// <summary>
    /// Adds the colour under the pointer to the place the pointer names — the dot it is on, or the
    /// line picked out in the list — rather than starting another place.
    /// </summary>
    private void AddAlternativeAt(Point place)
    {
        var index = MarkerAt(place) ?? _selected;
        if (index < 0)
        {
            Say(Strings.Get("LookPick.AlternativeNeedPoint"));
            return;
        }

        var local = LookGeometry.Inside(_frame.Width, _frame.Height, ZoomLevel, place.X, place.Y);
        AddAlternative(index, _frame[local.X, local.Y].ToHex());
    }

    /// <summary>
    /// Adds one more colour this place may show. The place stays where it is: what is being said is
    /// "this spot may look like either of these", which is a different thing from another spot. The
    /// engine reads the colours one after another with a bar between them, and is satisfied by any
    /// one of them.
    /// </summary>
    internal void AddAlternative(int index, string colour)
    {
        if (index < 0 || index >= _added.Count)
        {
            return;
        }

        var colours = ColourPointLine.Colours(_added[index]);
        if (colours.Any(one => string.Equals(one, colour, StringComparison.OrdinalIgnoreCase)))
        {
            Say(Strings.Get("LookPick.AlternativeRepeat"));
            return;
        }

        if (colours.Count >= ColourPointLine.MostColours)
        {
            Say(Strings.Format("LookPick.AlternativeFull", ColourPointLine.MostColours));
            return;
        }

        // 选中状态不动：加完颜色紧接着点画面，那一下仍然是「再加一处」，而不是把这一处挪走。
        _added[index] = ColourPointLine.WithColours(_added[index], [.. colours, colour]);
        RebuildRows();
        Draw();
        UpdateConfirm();
        Say(Strings.Format("LookPick.AlternativeAdded", colour, colours.Count + 1));
    }

    /// <summary>
    /// 从屏幕上另取一种颜色加给这一处：同一个按钮在另一套配色下的样子，这张图上没有，
    /// 只能在别处取。打开时这一窗会让开，和别的取色一样。
    /// </summary>
    private async void OnAddAlternative(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: int index } || index < 0 || index >= _added.Count)
        {
            return;
        }

        var picked = await ColorPickerWindow.PickAsync(this,
            SharedDevices.Current.Screen, SharedDevices.Current.Input, SharedDevices.Current.Hook);
        if (picked is { } pick)
        {
            AddAlternative(index, pick.Colour.ToHex());
        }
    }
}
