namespace WhaleGenie.GraphicsCapture;

/// <summary>
/// A picture of the rectangle that was asked for: four bytes to a pixel, blue first, one row after
/// another with no padding between them. The top left corner of the picture is the corner of the
/// rectangle that was asked for, so the part of it the source has something for sits where it
/// belongs and the rest comes back black; where that corner is on the screen is the caller's
/// business, because the caller is the one that said where to look.
/// </summary>
public sealed record ScreenGrab(int Width, int Height, byte[] Bgra, CaptureWay Used);
