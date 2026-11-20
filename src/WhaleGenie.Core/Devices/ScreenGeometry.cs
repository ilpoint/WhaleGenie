using System;
using System.Globalization;

namespace WhaleGenie.Core.Devices;

// Points, sizes and rectangles on the desktop, in pixels.
/// <summary>A point on the desktop, in pixels.</summary>
public readonly record struct ScreenPoint(int X, int Y);

/// <summary>A width and height, in pixels.</summary>
public readonly record struct ScreenSize(int Width, int Height);

/// <summary>
/// Where something sits on the screen: a corner and a size, in pixels. The same four numbers every
/// rectangle in a macro is written with, so a rule that compares two of them reads the way the
/// fields do.
/// </summary>
public readonly record struct ScreenRect(int Left, int Top, int Width, int Height)
{
    /// <summary>
    /// A rectangle with nothing in it, which is what a provider that refuses to say where an
    /// element is hands back. It matches no rule, because "nowhere" is not a place.
    /// </summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public int Right => Left + Width;

    public int Bottom => Top + Height;

    /// <summary>Whether a point is inside the rectangle; the right and bottom edges are outside.</summary>
    public bool Holds(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;

    public static ScreenRect Of(ScreenPoint location, ScreenSize size)
        => new(location.X, location.Y, size.Width, size.Height);
}
