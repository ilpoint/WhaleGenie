namespace WhaleGenie.ViewModels;

// The seam arithmetic the band's caret still measures against its drawing order.
public sealed partial class ValuePickerViewModel : ViewModelBase
{
    /// <summary>Maps a seam measured before a change onto the drawing left after it.</summary>
    public static int SeamAfterCut(int seam, CutSpan span)
        => seam >= span.End ? seam - span.Length
            : seam > span.Start ? span.Start
            : seam;
}
