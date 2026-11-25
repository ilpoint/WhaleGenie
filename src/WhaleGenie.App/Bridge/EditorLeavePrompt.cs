using System;
using System.Collections.Generic;
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
    private readonly object _gate = new();
    private readonly Dictionary<int, TaskCompletionSource<bool?>> _waiting = [];
    private BridgeServer? _server;
    private int _nextRequestId;
    private bool _disposed;

    public EditorLeavePrompt() : this(null)
    {
    }

    internal EditorLeavePrompt(TimeSpan? timeout)
    {
        _timeout = timeout ?? TimeSpan.FromMinutes(5);
    }

    public void Register(BridgeServer server)
    {
        _server = server;
        server.Register("editor.leaveAnswered", Answer);
        server.PageReset += OnPageReset;
    }

    public async Task<bool?> AskAsync(IReadOnlyList<string> documents)
    {
        if (_disposed || _server is not { IsReady: true } server)
        {
            return null;
        }

        var id = Interlocked.Increment(ref _nextRequestId);
        var answer = new TaskCompletionSource<bool?>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            _waiting[id] = answer;
        }

        try
        {
            await server.PushAsync("editor.leaveAsked",
                new { requestId = id, documents }, BridgePushMode.All);
        }
        catch (Exception)
        {
            lock (_gate)
            {
                _waiting.Remove(id);
            }

            return false;
        }

        var completed = await Task.WhenAny(answer.Task, Task.Delay(_timeout));
        if (completed != answer.Task)
        {
            lock (_gate)
            {
                _waiting.Remove(id);
            }

            // The page may still be showing the dialog. Tell it the host stopped waiting for this
            // request so it can close the same question instead of leaving it on screen.
            _ = server.PushAsync("editor.leaveCancelled", new { requestId = id },
                BridgePushMode.All);

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
                answer.TrySetResult(leave.GetBoolean());
            }
        }

        return Task.FromResult<object?>(null);
    }

    private void OnPageReset()
    {
        lock (_gate)
        {
            foreach (var answer in _waiting.Values)
            {
                answer.TrySetResult(null);
            }

            _waiting.Clear();
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
        }

        OnPageReset();
    }
}
