namespace WhaleGenie.GraphicsCapture;

/// <summary>
/// How a picture of part of the screen is taken. What is being read says which ways make sense: a
/// window can be asked to draw itself, or read through the machinery that composes it on screen,
/// which is what still finds a game another window is covering; the desktop can be copied out of
/// it, or handed over by the graphics card, which is the only way to read a game that fills the
/// screen on its own. The ways are not interchangeable, which is why the one that was used travels
/// back beside the picture.
/// </summary>
public enum CaptureWay
{
    /// <summary>
    /// Whatever suits the source, best first: the desktop through the graphics card's picture of it
    /// and then GDI; a window through Graphics Capture, then by asking it to draw itself, then off
    /// the screen.
    /// </summary>
    Auto,

    /// <summary>Copied straight out of the screen.</summary>
    Gdi,

    /// <summary>A window, asked to draw itself into a picture of our own.</summary>
    PrintWindow,

    /// <summary>A window or a display, read the way the screen shows it.</summary>
    GraphicsCapture,

    /// <summary>The display, handed over by the graphics card: the picture being composed for it.</summary>
    DesktopDuplication,
}

/// <summary>
/// How one way is written down. The same names are used wherever a way has to be spelled out — the
/// settings file and the record of what a step saw — so that both sides of the program are reading
/// one vocabulary rather than two.
/// </summary>
public static class CaptureWayNames
{
    /// <summary>
    /// What one way is written as, or empty for the automatic choice — which is a question rather
    /// than a way of taking a picture, and is never the answer to one.
    /// </summary>
    public static string Written(CaptureWay way) => way switch
    {
        CaptureWay.Gdi => "gdi",
        CaptureWay.PrintWindow => "printWindow",
        CaptureWay.GraphicsCapture => "graphicsCapture",
        CaptureWay.DesktopDuplication => "desktopDuplication",
        _ => string.Empty,
    };

    /// <summary>
    /// The way a written name stands for. Anything that reads as none of them is the automatic
    /// choice: a name nobody knows is not a way of taking a picture.
    /// </summary>
    public static CaptureWay Read(string written) => written.Trim().ToLowerInvariant() switch
    {
        "gdi" => CaptureWay.Gdi,
        "printwindow" => CaptureWay.PrintWindow,
        "graphicscapture" => CaptureWay.GraphicsCapture,
        "desktopduplication" => CaptureWay.DesktopDuplication,
        _ => CaptureWay.Auto,
    };
}
