using System;
using System.Threading.Tasks;
using WhaleGenie.Projects;

namespace WhaleGenie.Projects;

/// <summary>
/// The only writer of the project package. The page's Save, the window's Save, the exit question
/// and MCP all come through here, so two writes never replace each other's temporary file and an
/// open/reload can wait for a write that is already in flight.
/// </summary>
public interface IProjectSaver
{
    /// <summary>Whether automatic saving is currently asked for by the settings.</summary>
    bool IsEnabled { get; }

    /// <summary>True while a package write is between its copy and its move into place.</summary>
    bool IsSaving { get; }

    /// <summary>Why the last write failed, or null while it has not.</summary>
    string? Error { get; }

    /// <summary>Raised when the saving/error state or the document's saved state moved.</summary>
    event Action? Changed;

    /// <summary>The switch or the hotkey behind it changed; start or stop following at once.</summary>
    void OnSettingsChanged();

    /// <summary>Writes the project now, whether the automatic switch is on or not.</summary>
    Task<ProjectFileResult> SaveAsync(string? path = null);

    /// <summary>Writes whatever is still unsaved, waiting for a write already in flight.</summary>
    Task<ProjectFileResult> FlushAsync();

    /// <summary>
    /// Runs a project replacement with no write able to start or finish beside it. The pending
    /// quiet write is cancelled, an in-flight one is waited for, and automatic saving resumes at
    /// the end when the new document asks for it.
    /// </summary>
    Task<T> ReplaceAsync<T>(Func<Task<T>> action);

    /// <summary>Drops a quiet write that has not begun.</summary>
    void CancelPending();
}
