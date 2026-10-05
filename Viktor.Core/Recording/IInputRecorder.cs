using System;

namespace Viktor.Core.Recording;

/// <summary>
/// Listens to the keyboard and the mouse while a macro is being recorded. The events arrive
/// on whatever thread the hook runs on, so whoever listens has to be ready for that.
/// </summary>
public interface IInputRecorder : IDisposable
{
    /// <summary>True between <see cref="Start"/> and <see cref="Stop"/>.</summary>
    bool IsRunning { get; }

    /// <summary>Raised for every captured event, in the order it happened.</summary>
    event Action<RecordedInput>? Captured;

    /// <summary>Raised when the user presses the shortcut that ends a recording.</summary>
    event Action? StopRequested;

    /// <summary>Begins listening. Doing nothing while already running.</summary>
    void Start();

    /// <summary>Stops listening. Doing nothing while not running.</summary>
    void Stop();
}
