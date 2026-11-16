using System;
using System.Collections.Generic;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

/// <summary>
/// The frames standing around the windows a trigger rule recognises right now. A list can say
/// which windows a rule picks out; only a frame can show where two windows with the same title
/// are told apart. One preview belongs to one editor, and whoever put a frame up takes it down.
/// </summary>
internal sealed class TriggerFramePreview : IDisposable
{
    private readonly ScreenFrames _frames = new();

    /// <summary>Which windows are framed right now, so that a check can count them.</summary>
    public IReadOnlyCollection<long> Framed => _frames.Framed;

    /// <summary>
    /// Puts a frame around each place, and takes down the ones that have dropped out. Nothing is
    /// framed while the editor is out of the way — a minimised editor with frames left standing
    /// would be saying something nobody is reading.
    /// </summary>
    public void Show(bool canShow, IReadOnlyList<FramePlace> places)
        => _frames.Show(canShow ? AppWindows.Outside(places) : []);

    public void Dispose() => _frames.Dispose();
}
