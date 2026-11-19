using System;
using Avalonia.Controls;

namespace WhaleGenie.Views;

// How wide the three columns are given, and what happens when the window is narrower than them.
public partial class MacroEditorWindow : AppWindow
{
    /// <summary>
    /// The width the three columns need before they are squeezed: the action library's 280, the
    /// document's 344 and the settings rail's 360, plus the two 6 pixel splitters.
    /// </summary>
    private const double ColumnsWidth = 996;

    /// <summary>The room the window keeps around those columns, on each side.</summary>
    private const double Gutter = 14;

    private Grid? _columns;

    /// <summary>
    /// Gives the columns the width the window has to spare, or their own width when it has less.
    /// The grid is laid out at a definite width because a scroll viewer measures what scrolls
    /// without a limit: left to itself, the star column would size to the document's content
    /// instead of to the window's spare room.
    /// </summary>
    private void FollowColumnsWidth()
    {
        if (_columns is null)
        {
            return;
        }

        var available = Math.Max(ColumnsWidth, ClientSize.Width - (2 * Gutter));
        if (Math.Abs(_columns.Width - available) > 0.5)
        {
            _columns.Width = available;
        }
    }
}
