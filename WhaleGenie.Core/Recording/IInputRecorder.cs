using System;

namespace WhaleGenie.Core.Recording;

/// <summary>
/// Listens to the keyboard and the mouse while a macro is being recorded. The events arrive
/// on whatever thread the hook runs on, so whoever listens has to be ready for that.
/// </summary>
public interface IInputRecorder : IDisposable
{
    /// <summary>True between <see cref="Start"/> and <see cref="Stop"/>.</summary>
    bool IsRunning { get; }

    /// <summary>
    /// Whether input a program sent is left out. It is on by default: a macro that is clicked off
    /// while something else is typing would otherwise write that typing down as the user's own.
    /// </summary>
    bool IgnoreInjected { get; set; }

    /// <summary>
    /// Whether the buttons of the controller in the person's hands are written down as well. It is on
    /// by default: a recording of a game is mostly the controller, and a machine with none plugged in
    /// has nothing to write down either way.
    /// </summary>
    bool RecordController { get; set; }

    /// <summary>Raised for every captured event, in the order it happened.</summary>
    event Action<RecordedInput>? Captured;

    /// <summary>Raised when the user presses the shortcut that ends a recording.</summary>
    event Action? StopRequested;

    /// <summary>Begins listening. Doing nothing while already running.</summary>
    void Start();

    /// <summary>Stops listening. Doing nothing while not running.</summary>
    void Stop();
}
