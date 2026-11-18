using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

/// <summary>
/// The frames one window has standing on the screen. Several can be up at once — the windows a rule
/// recognises are worth seeing together, not one at a time — and each is taken down by whoever put
/// it up, which is the same rule as a single frame's, one level up.
/// </summary>
internal sealed class ScreenFrames : IDisposable
{
    private readonly Dictionary<long, ScreenFrameWindow> _up = [];

    /// <summary>Which windows are framed right now, so that a check can count them.</summary>
    internal IReadOnlyCollection<long> Framed => _up.Keys;

    /// <summary>
    /// Puts a frame around each of these, takes down the ones no longer asked for, and moves the
    /// ones that are still there. Moving rather than replacing matters while a list is being read
    /// again: a frame that blinked out and back would read as the window itself going away.
    /// </summary>
    public void Show(IEnumerable<FramePlace> places)
    {
        var wanted = new Dictionary<long, FramePlace>();
        foreach (var place in places)
        {
            if (place.Size.Width > 0 && place.Size.Height > 0)
            {
                wanted[place.Handle] = place;
            }
        }

        foreach (var (handle, frame) in _up.ToList())
        {
            if (wanted.TryGetValue(handle, out var place))
            {
                frame.Place(place.Location, place.Size);
                continue;
            }

            frame.Close();
            _up.Remove(handle);
        }

        foreach (var (handle, place) in wanted)
        {
            if (!_up.ContainsKey(handle)
                && ScreenFrameWindow.Show(place.Location, place.Size, place.Handle) is { } frame)
            {
                _up[handle] = frame;
                frame.Closed += (_, _) =>
                {
                    if (_up.TryGetValue(handle, out var current) && ReferenceEquals(current, frame))
                    {
                        _up.Remove(handle);
                    }
                };
            }
        }
    }

    /// <summary>Takes every frame down, which is what the window that put them up does when it goes.</summary>
    public void Clear()
    {
        foreach (var frame in _up.Values.ToList())
        {
            frame.Close();
        }

        _up.Clear();
    }

    public void Dispose() => Clear();
}
