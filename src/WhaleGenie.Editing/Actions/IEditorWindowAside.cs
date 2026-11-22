using System;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Editing;

/// <summary>The shell window stepping out of the way while a native overlay is up.</summary>
public interface IEditorWindowAside
{
    /// <summary>Moves the window aside if it is open; null when there is no window to move.</summary>
    Task<IDisposable?> StepAsideAsync(IScreenDevice screen);
}
