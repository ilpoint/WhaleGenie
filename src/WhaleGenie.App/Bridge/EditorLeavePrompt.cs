using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Contracts;
using WhaleGenie.Editor;
using WhaleGenie.Localization;

namespace WhaleGenie.Bridge;

/// <summary>
/// Asks the shell page about unsaved macro documents before the window goes away. It is the same
/// question the page's own title-bar close asks, so the answer is drawn once and in one place;
/// the native dialog is only used when no page is ready to draw it.
/// </summary>
/// <remarks>
/// The request is an event because the question starts in the host, and the answer is a command so
/// the waiting host can be woken. <see cref="BridgePushMode.All"/> matters: two questions must not
/// be coalesced into the latest one, or the first window close would wait for an answer that was
/// replaced.
/// </remarks>
public sealed class EditorLeavePrompt : IBridgeHandler, IEditorLeavePrompt, IDisposable
{
    private readonly TimeSpan _timeout;
    private readonly Func<BridgeSurface>? _surface;
    private readonly object _gate = new();
    private readonly Dictionary<int, TaskCompletionSource<bool?>> _waiting = [];
    private readonly Dictionary<int, BridgeSurface> _waitingSurface = [];
    private BridgeServer? _server;
    private int _nextRequestId;
    private bool _disposed;

    public EditorLeavePrompt() : this(null, null)
    {
    }

    public EditorLeavePrompt(TimeSpan? timeout, Func<BridgeSurface>? surface = null)
    {
        _timeout = timeout ?? TimeSpan.FromMinutes(5);
        _surface = surface;
    }

    public void Register(BridgeServer server)
    {
        _server = server;
        server.Register("editor.leaveAnswered", Answer);
        server.PageReset += OnPageReset;
        server.SurfaceGone += OnSurfaceGone;
    }

    public async Task<bool?> AskAsync(IReadOnlyList<string> documents)
    {
        var target = _surface?.Invoke() ?? BridgeSurface.Main;
        if (_disposed || _server is not { } server || !server.IsReadyOn(target))
        {
            return null;
        }

        var id = Interlocked.Increment(ref _nextRequestId);
        var answer = new TaskCompletionSource<bool?>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            _waiting[id] = answer;
            _waitingSurface[id] = target;
        }

        try
        {
            await server.PushAsync("editor.leaveAsked",
                new { requestId = id, documents }, BridgePushMode.All, target);
        }
        catch (Exception)
        {
            lock (_gate)
            {
                _waiting.Remove(id);
                _waitingSurface.Remove(id);
            }

            return false;
        }

        var completed = await Task.WhenAny(answer.Task, Task.Delay(_timeout));
        if (completed != answer.Task)
        {
            lock (_gate)
            {
                _waiting.Remove(id);
                _waitingSurface.Remove(id);
            }

            // The page may still be showing the dialog. Tell it the host stopped waiting for this
            // request so it can close the same question instead of leaving it on screen.
            _ = server.PushAsync("editor.leaveCancelled", new { requestId = id },
                BridgePushMode.All, target);

            // The page is alive but did not answer. Staying is the safe reading; a native question
            // at the same time would only duplicate the one the page is still showing.
            return false;
        }

        return await answer.Task;
    }

    private Task<object?> Answer(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("requestId", out var numbered)
            || !numbered.TryGetInt32(out var id)
            || !parameters.TryGetProperty("leave", out var leave)
            || leave.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsField"));
        }

        lock (_gate)
        {
            if (_waiting.Remove(id, out var answer))
            {
                _waitingSurface.Remove(id);
                answer.TrySetResult(leave.GetBoolean());
            }
        }

        return Task.FromResult<object?>(null);
    }

    private void OnPageReset() => ResetSurface(BridgeSurface.Main);

    private void OnSurfaceGone(BridgeSurface surface)
    {
        if (surface is not BridgeSurface.Main)
        {
            ResetSurface(surface);
        }
    }

    /// <summary>A page that went away answers the questions it was showing "no".</summary>
    private void ResetSurface(BridgeSurface surface)
    {
        lock (_gate)
        {
            foreach (var (id, answer) in _waiting.ToArray())
            {
                if (!_waitingSurface.TryGetValue(id, out var target) || target != surface)
                {
                    continue;
                }

                answer.TrySetResult(null);
                _waiting.Remove(id);
                _waitingSurface.Remove(id);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_server is not null)
        {
            _server.PageReset -= OnPageReset;
            _server.SurfaceGone -= OnSurfaceGone;
        }

        OnPageReset();
    }
}
