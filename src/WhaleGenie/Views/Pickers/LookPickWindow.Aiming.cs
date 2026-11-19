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

// The pointer over the picture: where it is aimed, what a press picks, and the menu a right press opens.
public partial class LookPickWindow : AppWindow
{
    /// <summary>
    /// 一个像素在画布上的中心。指针停在哪个像素、偏移算到哪个像素，说的都是这一格，而不是它的左上角：
    /// 画标记时补上半格，否则放大到 800% 时整枚标记会偏半格，看着就是「位置不对」。
    /// </summary>
    private Point ToBoardCentre(ScreenPoint point)
        => new((point.X - _origin.X + 0.5) * ZoomLevel,
               (point.Y - _origin.Y + 0.5) * ZoomLevel);

    /// <summary>
    /// 把放大镜摆到指针旁边，读的是这张图自己的像素：图上的一像素落进宏里就是一个偏移，
    /// 所以在图上瞄的时候要看得见它落在哪一格。
    /// </summary>
    private void Aim(PointerEventArgs e)
    {
        if (_board is null || _loupeLayer is null || _frame.IsEmpty)
        {
            return;
        }

        var place = e.GetPosition(_board);
        var local = LookGeometry.Inside(_frame.Width, _frame.Height, ZoomLevel, place.X, place.Y);
        var at = e.GetPosition(_loupeLayer);
        var room = _loupeLayer.Bounds;
        _magnifier.Follow(_frame, local.X, local.Y);
        _magnifier.PlaceNear(at.X, at.Y,
            room.Width > 0 ? room.Width : Width,
            room.Height > 0 ? room.Height : Height);
    }

    // ------------------------------------------------------------------ picking

    /// <summary>
    /// A click on the picture is a place on the screen: the picture's own corner is where the look
    /// started, so a point on it plus that corner is the place the step would have worked at.
    /// </summary>
    private void OnPickPoint(object? sender, PointerPressedEventArgs e)
    {
        if (_board is null || _frame.IsEmpty)
        {
            return;
        }

        var place = e.GetPosition(_board);

        switch (e.GetCurrentPoint(_board).Properties.PointerUpdateKind)
        {
            // 右键属于图上的点：它给出改这一条、删这一条。空白处按右键不做什么——画面上的
            // 右键若是"又加一条"，想改一条时就会先多出一条。
            case PointerUpdateKind.RightButtonPressed:
                if (MenuFor(place) is { } menu)
                {
                    e.Handled = true;
                    menu.Open(_board);
                }

                return;

            default:
                break;
        }

        // 只有左键是「点一处」。滚轮键以前也新增一条，撤掉了：中键常被当成拖动画面的手势，
        // 手一滑就多出一处，而多出来的那一处不点开列表是看不出来的。
        if (!e.GetCurrentPoint(_board).Properties.IsLeftButtonPressed)
        {
            return;
        }

        // 按住 Shift 点一下，是给一处再加一种颜色，不是新加一处：同一个按钮换一套配色长什么样，
        // 只能靠另一个像素上的颜色说得清。
        if (_mode == LookPickMode.Colours && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            e.Handled = true;
            AddAlternativeAt(place);
            return;
        }

        // A press on one of the dots takes hold of that line: dragging the marker is how a point that
        // landed a pixel or two off is put right, and it is the gesture the picture itself invites.
        if (_mode == LookPickMode.Colours && MarkerAt(place) is { } marker)
        {
            _dragging = marker;
            _selected = marker;
            _picked = _addedAt[marker];
            e.Pointer.Capture(_board);
            RebuildRows();
            Draw();
            e.Handled = true;
            return;
        }

        // Dragging a picture that fits the window has nothing to move, and the press is a pick; one
        // that does not fit is moved, and a press is not a pick until the button comes back up.
        if (this.FindControl<ScrollViewer>("Scroll") is { } room && Moves(room))
        {
            _panning = true;
            _panFrom = e.GetPosition(room);
            _panOrigin = room.Offset;
            e.Pointer.Capture(_board);
            e.Handled = true;
            return;
        }

        Pick(LookGeometry.Inside(_frame.Width, _frame.Height, ZoomLevel, place.X, place.Y));
    }

    /// <summary>
    /// The menu for the dot under the pointer, or null when there is none there. It is built for the
    /// dot rather than for the picture: with nothing under the pointer there is nothing to edit.
    /// </summary>
    internal ContextMenu? MenuFor(Point place)
    {
        if (_mode != LookPickMode.Colours || MarkerAt(place) is not { } index)
        {
            return null;
        }

        return Menu(index);
    }

    /// <summary>The line whose dot — or whose number, which belongs to the same dot — is under the pointer.</summary>
    private int? MarkerAt(Point place)
    {
        for (var index = 0; index < _addedAt.Count; index++)
        {
            var at = ToBoardCentre(_addedAt[index]);
            if (Math.Abs(at.X - place.X) <= 8 && Math.Abs(at.Y - place.Y) <= 8)
            {
                return index;
            }

            // 序号牌也算这一处：指它、右键它，说的都是同一条。
            if (place.X >= at.X + 2 && place.X <= at.X + 22
                && place.Y >= at.Y - 19 && place.Y <= at.Y + 3)
            {
                return index;
            }
        }

        return null;
    }

    /// <summary>
    /// Drags the line being held to wherever the pointer is now: the dot follows, the numbers of that
    /// line follow with it, and the row beside the picture is rewritten as it goes.
    /// </summary>
    private void DragTo(Point place)
    {
        if (_dragging < 0 || _dragging >= _added.Count)
        {
            return;
        }

        var local = LookGeometry.Inside(_frame.Width, _frame.Height, ZoomLevel, place.X, place.Y);
        var point = PointAt(local);
        var offset = OffsetAt(point);
        var hex = _frame[local.X, local.Y].ToHex();

        // 拖的是「这一处」，不是「这一行的颜色」：备选色和按行写下的容差跟着一起走。
        _added[_dragging] = ColourPointLine.WithCore(_added[_dragging],
            offset.X.ToString(CultureInfo.InvariantCulture),
            offset.Y.ToString(CultureInfo.InvariantCulture), hex);
        _addedAt[_dragging] = point;
        _picked = point;

        RebuildRows();
        Draw();
        Say(Strings.Format("LookPick.Moved", offset.X, offset.Y));
    }

    /// <summary>Drops the line that was picked out, which is what clicking the empty part of the list does.</summary>
    internal void ClearSelection()
    {
        if (_selected < 0)
        {
            return;
        }

        _selected = -1;
        _picked = null;
        RebuildRows();
        Draw();
        Say(Strings.Get("LookPick.NothingPicked"));
    }

    /// <summary>Whether the event came from inside one part of the window.</summary>
    private static bool Inside(object? source, Visual part)
    {
        for (var node = source as Visual; node is not null; node = node.GetVisualParent())
        {
            if (ReferenceEquals(node, part))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a press landed on one of the rows rather than on the space around them.</summary>
    private static bool OnRow(object? source)
    {
        for (var node = source as Visual; node is not null; node = node.GetVisualParent())
        {
            if (node is Border { Tag: int })
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the picture is bigger than the room it is in, and so has somewhere to move.</summary>
    private static bool Moves(ScrollViewer room)
        => room.Extent.Width > room.Viewport.Width + 0.5 || room.Extent.Height > room.Viewport.Height + 0.5;
}
