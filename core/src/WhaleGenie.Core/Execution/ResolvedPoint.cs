using WhaleGenie.Core.Devices;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// A point as the device needs it and in both coordinate spaces a macro may read: the device's own
/// space, the screen, and the window client area when the step counts from one. One type rather than
/// three side by side, so a click and a drag cannot hand back different subsets of the same place.
/// </summary>
internal readonly record struct ResolvedPoint(ScreenPoint Device, ScreenPoint Screen,
    ScreenPoint? Window);
