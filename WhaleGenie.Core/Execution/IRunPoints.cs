using WhaleGenie.Core.Devices;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// Where a step's point is: the numbers it wrote counted from the screen or from a window's client
/// area, a record that carries its own basis, and both ends of a drag measured from one corner. A
/// family reads points through this rather than through the runner, so a click, a drag and a move
/// can be read and tried without the engine.
/// </summary>
internal interface IRunPoints
{
    /// <summary>Where a mouse action lands: the pointer, a record, or the numbers written.</summary>
    ResolvedPoint Point(ExecutableStep step, string xName, string yName);

    /// <summary>Where a move lands: the place a record says, or the two numbers written down.</summary>
    ResolvedPoint MovePlace(ExecutableStep step);

    /// <summary>One end of a drag, measured from the anchor the step names.</summary>
    ResolvedPoint DragPlace(ExecutableStep step, string end,
        (ScreenPoint? Corner, WindowInfo? Window) anchor);

    /// <summary>The corner a step's numbers are measured from, and the window behind it.</summary>
    (ScreenPoint? Corner, WindowInfo? Window) AnchorOf(ExecutableStep step);

    /// <summary>Whether this step's input is posted at a window rather than driving the desktop.</summary>
    bool PostsMessages(ExecutableStep step);

    /// <summary>
    /// A point with the drift the step asked for on it, so a run of clicks is spread over a button
    /// rather than landing on one pixel. A step that asked for none gets the point itself.
    /// </summary>
    ScreenPoint Scatter(ExecutableStep step, ScreenPoint point);

    /// <summary>Hands over the point the step actually used, in the spaces it knows.</summary>
    void GivePoints(ExecutableStep step, ScreenPoint screen, ScreenPoint? window,
        string screenKey = "screenPoint", string windowKey = "windowPoint");
}
