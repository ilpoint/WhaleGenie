using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Localization;
using WhaleGenie.Mcp;
using WhaleGenie.Models;

namespace WhaleGenie.Projects;

/// <summary>
/// Keeps the package on disk in step with the document. One change opens a short quiet period; a
/// write follows if nothing else moved, so typing a name does not write a file on every keystroke.
/// The same worker serializes every write — the page's Save, Save As, the exit question and the MCP
/// tools all go through <see cref="SaveAsync"/> rather than each opening the file themselves.
/// </summary>
/// <remarks>
/// A save copies the project on the caller's thread and writes the zip on a worker, the way
/// <see cref="ProjectFiles.SaveAsync"/> already did. What is new is the revision check: work that
/// arrives while the file is being written is newer than the file, so it stays unsaved and a second
/// pass carries it, instead of being silently cleared along with the first.
/// </remarks>
public sealed class ProjectSaver : IProjectSaver, IDisposable
{
    private readonly ProjectWorkspace _workspace;
    private readonly AppSettings _settings;
    private readonly IUiDispatcher _ui;
    private readonly TimeSpan _delay;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly object _pendingGate = new();

    private CancellationTokenSource? _pending;
    private volatile bool _saving;
    private volatile bool _suspended;
    private volatile string? _error;
    private bool _disposed;

    /// <summary>
    /// Starts following the document. The quiet period is a constructor value so a check can make
    /// it short; the program uses a little under a second.
    /// </summary>
    public ProjectSaver(ProjectWorkspace workspace, AppSettings settings, IUiDispatcher ui,
        TimeSpan? delay = null)
    {
        _workspace = workspace;
        _settings = settings;
        _ui = ui;
        _delay = delay ?? TimeSpan.FromMilliseconds(800);

        _workspace.Changed += OnWorkspaceChanged;
        _workspace.Replaced += OnWorkspaceReplaced;
        _workspace.PropertyChanged += OnWorkspacePropertyChanged;
        _workspace.Triggers.Changed += OnTriggersChanged;
        ScheduleIfNeeded();
    }

    /// <summary>True while a package write is between its copy and its move into place.</summary>
    public bool IsSaving => _saving;

    /// <summary>Whether the switch currently asks for writes without an explicit Save.</summary>
    public bool IsEnabled => _settings.LoadAutoSaveProject();

    /// <summary>Why the last write failed, or empty while it has not.</summary>
    public string? Error => _error;

    /// <summary>Raised when the saving/error state or the document's saved state moved.</summary>
    public event Action? Changed;

    /// <summary>The switch or the hotkey behind it changed; start or stop following at once.</summary>
    public void OnSettingsChanged()
    {
        if (!_settings.LoadAutoSaveProject())
        {
            CancelPending();
        }
        else
        {
            ScheduleIfNeeded();
        }

        RaiseChanged();
    }

    /// <summary>Drops a write that has not begun; the document stays unsaved.</summary>
    public void CancelPending()
    {
        lock (_pendingGate)
        {
            _pending?.Cancel();
            _pending?.Dispose();
            _pending = null;
        }
    }

    /// <summary>
    /// Writes the project now, whatever the auto-save switch says. A manual Save means save; the
    /// switch governs the quiet writes, not what the person asked for.
    /// </summary>
    public async Task<ProjectFileResult> SaveAsync(string? path = null)
    {
        var target = path ?? _workspace.CurrentPath;
        if (target is not { Length: > 0 })
        {
            return ProjectFileResult.Failed(new InvalidOperationException(
                Strings.Get("Project.NoPackagePath")));
        }

        CancelPending();
        await _writeGate.WaitAsync();
        var wrote = false;
        try
        {
            if (_disposed)
            {
                return ProjectFileResult.Failed(new ObjectDisposedException(nameof(ProjectSaver)));
            }

            _saving = true;
            _error = null;
            RaiseChanged();

            // The copy is made here, on the interface thread; the zip is written on a worker
            // inside ProjectFiles.SaveAsync and the plan's revision decides whether clear is safe.
            var result = await ProjectFiles.SaveAsync(_workspace, target);
            wrote = result.Ok;
            if (result.Ok)
            {
                // A save whose project moved to another file while the zip was written belongs to
                // the file it started from; remembering it as the last project would reopen the
                // project the person left.
                if (string.Equals(_workspace.CurrentPath, target, StringComparison.OrdinalIgnoreCase))
                {
                    _settings.StoreLastProject(target);
                }

                _error = null;
            }
            else
            {
                _error = Strings.Format("Project.AutoSaveFailed", Path.GetFileName(target),
                    result.Error?.Message ?? string.Empty);
            }

            return result;
        }
        finally
        {
            _saving = false;
            _writeGate.Release();
            RaiseChanged();

            // A change that arrived while the file was being written needs one more pass. A failed
            // write does not: retrying a read-only folder every second would turn one error into a
            // stream of them, and the next edit (or a Save) is the honest moment to try again.
            if (wrote && !_suspended && _settings.LoadAutoSaveProject() && _workspace.IsDirty)
            {
                ScheduleIfNeeded();
            }
        }
    }

    /// <summary>
    /// Runs a project replacement with the write gate held: the quiet write that has not started is
    /// cancelled, the one already writing is waited for, and no new quiet write starts until the
    /// replacement has finished. Without this an open could read the package while a save was
    /// about to replace it, or a save could write the old document over the newly opened one.
    /// </summary>
    public async Task<T> ReplaceAsync<T>(Func<Task<T>> action)
    {
        CancelPending();
        await _writeGate.WaitAsync();
        _suspended = true;
        try
        {
            return await action();
        }
        finally
        {
            _suspended = false;
            _writeGate.Release();
            if (!_disposed && _settings.LoadAutoSaveProject() && _workspace.IsDirty)
            {
                ScheduleIfNeeded();
            }
        }
    }

    /// <summary>
    /// Writes whatever is still unsaved, waiting for a write already in flight. Used by the exit
    /// and hide paths before they decide whether there is anything to ask about.
    /// </summary>
    public async Task<ProjectFileResult> FlushAsync()
    {
        CancelPending();
        await _writeGate.WaitAsync();
        _writeGate.Release();

        return _workspace.IsDirty
            ? await SaveAsync()
            : ProjectFileResult.Success();
    }

    private void ScheduleIfNeeded()
    {
        if (_disposed
            || _suspended
            || !_settings.LoadAutoSaveProject()
            || !_workspace.IsDirty
            || _workspace.CurrentPath is not { Length: > 0 })
        {
            return;
        }

        CancellationToken token;
        lock (_pendingGate)
        {
            _pending?.Cancel();
            _pending?.Dispose();
            _pending = new CancellationTokenSource();
            token = _pending.Token;
        }

        _ = QuietlyWriteAsync(token);
    }

    private async Task QuietlyWriteAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(_delay, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested || _disposed)
        {
            return;
        }

        // The copy needs the interface thread the project lives on; the delay itself does not.
        await _ui.InvokeAsync(() =>
        {
            if (!token.IsCancellationRequested)
            {
                _ = SaveAsync();
            }

            return true;
        });
    }

    private void OnWorkspaceChanged(object? sender, EventArgs args) => ScheduleIfNeeded();

    private void OnWorkspaceReplaced(object? sender, EventArgs args)
    {
        CancelPending();
        ScheduleIfNeeded();
    }

    private void OnTriggersChanged(object? sender, EventArgs args) => ScheduleIfNeeded();

    private void OnWorkspacePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ProjectWorkspace.CurrentPath))
        {
            CancelPending();
        }

        if (args.PropertyName is nameof(ProjectWorkspace.IsDirty)
            or nameof(ProjectWorkspace.CurrentPath))
        {
            ScheduleIfNeeded();
        }
    }

    private void RaiseChanged()
    {
        if (_disposed)
        {
            return;
        }

        // Raised on the caller's thread; the bridge queue is the only subscriber that matters and
        // it is safe from any thread, while the tests read the fields directly.
        Changed?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _workspace.Changed -= OnWorkspaceChanged;
        _workspace.Replaced -= OnWorkspaceReplaced;
        _workspace.PropertyChanged -= OnWorkspacePropertyChanged;
        _workspace.Triggers.Changed -= OnTriggersChanged;
        CancelPending();

        // The semaphore is deliberately not disposed: a quiet write may still be waiting on it
        // while the container is being torn down, and a disposed semaphore would turn that wait
        // into an exception on a path that has no caller left to care.

    }
}
