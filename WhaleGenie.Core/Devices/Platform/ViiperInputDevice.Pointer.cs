using System;
using System.Collections.Generic;
using System.Threading;
using SharpHook.Data;


namespace WhaleGenie.Core.Devices.Platform;

// The mouse: moving, clicking, scrolling and dragging along a path.
public sealed partial class ViiperInputDevice : IInputDevice, IGamepadDevice, IDisposable
{
    public void MoveMouse(int x, int y, int durationMs)
    {
        lock (_gate)
        {
            Glide(new ScreenPoint(x, y), durationMs);
        }
    }

    public void MoveMouseAlong(IReadOnlyList<ScreenPoint> path, int durationMs)
    {
        if (path.Count == 0)
        {
            return;
        }

        lock (_gate)
        {
            // Both ends of the path are made sure of: the first is where the pointer is meant to
            // already be, and the last is where the macro wants it. Everything between is only the
            // way it gets there.
            _ = Seek(path[0]);

            if (path.Count > 1 && durationMs > 0)
            {
                Walk(path, Math.Max(1, durationMs / (path.Count - 1)));
            }

            _ = Seek(path[^1]);
        }
    }

    public void MoveMouseRelative(int dx, int dy, int durationMs)
    {
        lock (_gate)
        {
            var from = Pointer();
            Glide(new ScreenPoint(from.X + dx, from.Y + dy), durationMs);
        }
    }

    public void MouseDown(string button, int x, int y) => Press(button, down: true, x, y);

    public void MouseUp(string button, int x, int y) => Press(button, down: false, x, y);

    public void Click(string button, int x, int y, int clicks, int intervalMs)
    {
        lock (_gate)
        {
            var (bit, name) = Button(button);
            Glide(new ScreenPoint(x, y), 0);

            var times = Math.Max(1, clicks);
            var gap = intervalMs > 0 ? intervalMs : ClickGapMs;
            for (var count = 0; count < times; count++)
            {
                if (count > 0)
                {
                    Sleep(gap);
                }

                WhaleGenieInputGate.Note(name);
                _buttons |= bit;
                Send(0, 0);
                Sleep(ClickHoldMs);
                _buttons &= (byte)~bit;
                Send(0, 0);
            }
        }
    }

    public void Scroll(string direction, int delta, int x, int y)
    {
        lock (_gate)
        {
            Glide(new ScreenPoint(x, y), 0);

            var (sign, vertical) = direction.Trim().ToLowerInvariant() switch
            {
                "up" => (1, true),
                "left" => (-1, false),
                "right" => (1, false),
                _ => (-1, true),
            };

            var left = Math.Abs(delta);
            var notches = Math.Max(1, (left + (Notch / 2)) / Notch);
            for (var turned = 0; turned < notches; turned++)
            {
                // One notch per report: the device counts in whole notches and the machine turns
                // each of them into a wheel message of 120 units, which is the unit a step asks in.
                // A step that asked for part of a notch still gets a whole one, because a partial
                // notch cannot be said at all.
                var turn = (short)sign;

                // The name is the one the recorder hears, which counts up as a turn towards the
                // left, while the virtual device counts up as a turn towards the right.
                WhaleGenieInputGate.Note(SharpKeys.WheelName(
                    vertical ? MouseWheelScrollDirection.Vertical : MouseWheelScrollDirection.Horizontal,
                    vertical ? turn : (short)-turn));

                Send(0, 0, vertical ? turn : (short)0, vertical ? (short)0 : turn);
                Sleep(NotchGapMs);
            }
        }
    }

    public void Drag(string button, int startX, int startY, int endX, int endY, int durationMs, int steps)
        => DragAlong(button, MousePath.Plan(MouseRoute.Direct, new ScreenPoint(startX, startY),
            new ScreenPoint(endX, endY), Math.Clamp(steps, 1, 200)), durationMs);

    public void DragAlong(string button, IReadOnlyList<ScreenPoint> path, int durationMs)
    {
        if (path.Count == 0)
        {
            return;
        }

        lock (_gate)
        {
            var (bit, name) = Button(button);
            Glide(path[0], 0);
            WhaleGenieInputGate.Note(name);
            _buttons |= bit;
            Send(0, 0);

            try
            {
                Walk(path, path.Count > 1 && durationMs > 0
                    ? Math.Max(1, durationMs / (path.Count - 1))
                    : 0);

                // The end of a drag is where the button is let go, so it is worth landing on.
                _ = Seek(path[^1]);
            }
            finally
            {
                // Let go whatever happened, or the machine is left with a button held that nobody
                // is going to release.
                _buttons &= (byte)~bit;
                Send(0, 0);
            }
        }
    }
}
