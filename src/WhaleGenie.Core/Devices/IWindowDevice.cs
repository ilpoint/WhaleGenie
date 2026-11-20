using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WhaleGenie.Core.Recording;

namespace WhaleGenie.Core.Devices;

// Windows: finding one, reading it and moving it.
/// <summary>Open windows: finding them, moving them, and closing them.</summary>
public interface IWindowDevice
{
    /// <summary>Every top-level window that has a title, in the order Windows lists them.</summary>
    IReadOnlyList<WindowInfo> List();

    /// <summary>
    /// The window Windows currently calls the foreground window, or null when nothing has one.
    /// The default is the first visible window a device lists, which is what a stand-in desktop
    /// has to offer; the Windows device asks the machine directly.
    /// </summary>
    WindowInfo? Foreground() => List().FirstOrDefault();

    /// <summary>
    /// The window under a place on the screen, or null when there is nothing there. The default
    /// walks the list a device gives, first to last, which is front to back for a real desktop;
    /// the Windows device asks the machine, so that a window with no title in the title bar — the
    /// one thing <see cref="List"/> leaves out — can still be picked.
    /// </summary>
    WindowInfo? At(int x, int y) => List().FirstOrDefault(window =>
        !window.Minimized
        && window.Size.Width > 0 && window.Size.Height > 0
        && x >= window.Location.X && x < window.Location.X + window.Size.Width
        && y >= window.Location.Y && y < window.Location.Y + window.Size.Height);

    /// <summary>
    /// Every window that matches <paramref name="value"/> the way <paramref name="match"/> and
    /// <paramref name="compare"/> say, ignoring case, in the order <see cref="List"/> gives them,
    /// or nothing when none does. An empty value matches the frontmost window alone — the one window
    /// a step that says nothing means — unless <paramref name="everyWhenEmpty"/> is set, which is
    /// what a step that lists windows means by saying nothing: every window there is. The interface
    /// asks for all of them so that what a step would recognise can be shown rather than guessed at,
    /// and the rule itself stays in one place.
    /// </summary>
    IReadOnlyList<WindowInfo> Filter(string value, WindowMatch match,
        string compare = TextCompare.Contains, bool everyWhenEmpty = false,
        TextCompareOptions options = default);

    /// <summary>
    /// The first window that matches <paramref name="value"/> the way <paramref name="match"/>
    /// and <paramref name="compare"/> say, ignoring case, or null when nothing does. An empty value
    /// matches the frontmost window, whichever of the three parts it is asked to look at. A
    /// <paramref name="compare"/> of <see cref="TextCompare.Regex"/> with a pattern the machine
    /// cannot read throws <see cref="ArgumentException"/>, which the run turns into a failed step.
    /// </summary>
    WindowInfo? Find(string value, WindowMatch match,
        string compare = TextCompare.Contains, TextCompareOptions options = default);

    /// <summary>
    /// The name of the program that owns a window, without the ".exe", or an empty string when the
    /// window is gone. Looking a process up costs more than reading a title, which is why a step
    /// that only compares titles never causes one.
    /// </summary>
    string ProcessOf(long handle);

    /// <summary>
    /// The process that owns a window, or 0 when the window is gone or the system will not say.
    /// Cheaper than <see cref="ProcessOf"/>: it asks which process, without opening one.
    /// </summary>
    int ProcessIdOf(long handle);

    /// <summary>The class a window was registered under, such as "Notepad", or an empty string.</summary>
    string ClassOf(long handle);

    /// <summary>Brings a window to the front, restoring it first if it was shrunk.</summary>
    bool Activate(long handle);

    bool Minimize(long handle);

    bool Maximize(long handle);

    /// <summary>Puts a shrunk or full-screen window back to its normal size.</summary>
    bool Restore(long handle);

    /// <summary>Asks a window to close, the same as clicking its close button.</summary>
    bool Close(long handle);

    /// <summary>Moves and resizes a window, in screen pixels.</summary>
    bool Move(long handle, int x, int y, int width, int height);

    /// <summary>
    /// Where a window's client area starts, in screen pixels: the point inside its border that
    /// the window itself counts from. A macro that means "100 pixels into the window" needs this
    /// rather than the window's outer corner, which the border and the title bar push around.
    /// </summary>
    ScreenPoint ClientOrigin(long handle);

    /// <summary>
    /// How big a window's client area is, in pixels: the part inside the border and under the title
    /// bar, which is the part the window draws in and the only part a search inside it can look at.
    /// </summary>
    ScreenSize ClientSize(long handle);
}
