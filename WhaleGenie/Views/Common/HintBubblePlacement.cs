using System;
using Avalonia;

namespace WhaleGenie.Views;

/// <summary>
/// 气泡放在哪里：锚点与可用区域都在顶层坐标里，默认放在锚点下方，下方不够且上方更大时翻到
/// 上方，水平贴着锚点左缘并在可用区域内收边。只算一次，结果不会随内容变化跳动。
/// </summary>
internal static class HintBubblePlacement
{
    private const double Gap = 6;

    internal static Rect Place(Rect anchor, Rect available, Size desired)
    {
        var width = Math.Min(desired.Width, Math.Max(0, available.Width));
        var height = Math.Min(desired.Height, Math.Max(0, available.Height));

        var x = Math.Clamp(anchor.X, available.X, Math.Max(available.X, available.Right - width));

        var below = anchor.Bottom + Gap;
        var above = anchor.Top - Gap - height;
        var roomBelow = available.Bottom - below;
        var roomAbove = anchor.Top - Gap - available.Top;

        var y = roomBelow >= height
            ? below
            : roomAbove > roomBelow && above >= available.Top
                ? above
                : Math.Clamp(below, available.Top, Math.Max(available.Top, available.Bottom - height));

        y = Math.Clamp(y, available.Y, Math.Max(available.Y, available.Bottom - height));
        return new Rect(x, y, width, height);
    }
}
