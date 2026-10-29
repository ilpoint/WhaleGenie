using System;
using System.Text.RegularExpressions;

namespace WhaleGenie.Tests;

/// <summary>
/// What counts as "this window has a title bar of its own": a button carrying the library's `Chrome`
/// class. Two whole-repository checks hang off this — every such window must be draggable and must
/// show the mark — so the marker is the one place they both read.
///
/// The pattern is matched **inside a `Classes` attribute**: the bare word also appears in the
/// `SurfaceChrome` colour token, and matching that would send the checks to windows that have no
/// title-bar buttons at all. `Expected` is the tripwire: a marker that matches nothing makes a check
/// pass by covering nothing, and "no window has a title bar" looks exactly like "the class was
/// renamed".
/// </summary>
internal static class TitleBarMarkup
{
    /// <summary>How many windows draw their own title bar. Update it, and look at the new window.</summary>
    internal const int Expected = 18;

    private static readonly Regex Marker =
        new(@"Classes=""[^""]*\bChrome\b", RegexOptions.CultureInvariant);

    internal static bool IsTitleBar(string markup) => Marker.IsMatch(markup);
}
