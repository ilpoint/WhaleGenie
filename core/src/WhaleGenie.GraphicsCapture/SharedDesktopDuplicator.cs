namespace WhaleGenie.GraphicsCapture;

/// <summary>
/// The one duplication of the primary display this program hands out.
///
/// A process may duplicate an output once: the second <c>CreateSession</c>-worth of asking is
/// refused, and whoever asked would be left reading the screen the slow way for no better reason
/// than that a run and the editor's preview are both looking at it. So the whole program takes
/// shares in one, and it leaves the machine when the last share is given back.
/// </summary>
internal static class SharedDesktopDuplicator
{
    private static readonly object Pool = new();

    private static DesktopDuplicator? _duplicator;

    private static int _shares;

    /// <summary>Takes a share of the display's picture of itself.</summary>
    public static DesktopDuplicator Take()
    {
        lock (Pool)
        {
            _duplicator ??= new DesktopDuplicator();
            _shares++;
            return _duplicator;
        }
    }

    /// <summary>
    /// Gives back a share taken with <see cref="Take"/>. Whoever gives back the last share lets the
    /// duplication go.
    /// </summary>
    public static void Give()
    {
        DesktopDuplicator? leaving;
        lock (Pool)
        {
            // A share given back that was never taken would take the duplication away from a reader
            // still using it.
            if (_shares == 0)
            {
                return;
            }

            if (--_shares > 0)
            {
                return;
            }

            leaving = _duplicator;
            _duplicator = null;
        }

        leaving?.Dispose();
    }
}
