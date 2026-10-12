using System;
using System.Linq;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Views;

/// <summary>
/// The button beside a control picker: it looks the selector up and flashes a frame where the
/// control sits. The question after picking is "did that take the right thing?", and seeing the
/// frame land on it answers that without running the macro.
/// </summary>
internal static class ElementFlash
{
    /// <summary>How long the frame stays up: long enough to see, short enough to not be in the way.</summary>
    private static readonly TimeSpan Life = TimeSpan.FromMilliseconds(1500);

    /// <summary>
    /// Finds what the selector names — through the desktop's UI Automation — and flashes where it
    /// is. Null when nothing is there, which is the answer the dialog reports rather than a frame
    /// over the wrong thing. The window filter narrows the search the same way the step's own
    /// window field does.
    /// </summary>
    public static UiElementInfo? Locate(IUiDevice? ui, string selector, string window)
    {
        var wanted = (selector ?? string.Empty).Trim();
        if (ui is null || wanted.Length == 0)
        {
            return null;
        }

        try
        {
            var query = UiQuery.Parse(wanted, string.IsNullOrWhiteSpace(window) ? null : window);
            return ui.FindAll(query, 1).FirstOrDefault();
        }
        catch (Exception)
        {
            // A control that cannot be looked up right now is an answer too: the dialog says
            // nothing matched rather than failing over it.
            return null;
        }
    }

    /// <summary>
    /// Flashes the rectangle an element occupies, or answers null when there is nothing there to
    /// show. The caller owns the frame from then on and can wait on its <c>Finished</c>.
    /// </summary>
    public static ScreenFrameWindow? Show(UiElementInfo element)
        => ScreenFrameWindow.Flash(element.Location, element.Size, Life);
}
