using System;
using System.Text.RegularExpressions;

namespace WhaleGenie.Tests;

/// <summary>
/// What counts as "this window has a title bar of its own": a button from one of the two families
/// that live up there — `Chrome` for the tool buttons and `Caption` for the close key. Two
/// whole-repository checks hang off this — every such window must be draggable and must show the
/// mark — so the marker is the one place they both read.
///
/// Both families have to be in the pattern: eleven windows carry nothing but their close key up
/// there, and a marker that only knew `Chrome` would quietly drop them from both checks.
///
/// The pattern is a **`<Button>` tag with one of those classes**: the bare words turn up elsewhere —
/// `SurfaceChrome` is a colour token, `TextBlock.Caption` is a text class in several windows — and
/// matching those would send the checks to windows that have no title-bar buttons at all.
/// `Expected` is the tripwire: a marker that matches nothing makes a check pass by covering nothing,
/// and "no window has a title bar" looks exactly like "the class was renamed".
/// </summary>
internal static class TitleBarMarkup
{
    /// <summary>
    /// How many windows draw their own title bar. Update it, and look at the new window.
    /// </summary>
    /// <remarks>
    /// 19 rather than 18 since the close key became a `Caption`: `ConfirmDialog` has always drawn a
    /// 38-pixel title row of its own, but its close key was a plain `Ghost`, so the marker never saw
    /// it and the two checks below never looked at that window. It passes them — the whale is in the
    /// row and the row drags.
    /// </remarks>
    internal const int Expected = 19;

    private static readonly Regex Marker =
        new(@"<Button\b[^>]*Classes=""[^""]*\b(?:Chrome|Caption)\b", RegexOptions.CultureInvariant);

    internal static bool IsTitleBar(string markup) => Marker.IsMatch(markup);
}
