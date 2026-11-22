using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Contracts;
using WhaleGenie.Localization;

namespace WhaleGenie.Bridge;

/// <summary>
/// The program asking the page a question. The old windows put a dialog on the screen; the web
/// shell asks over the bridge instead: a <c>dialog.request</c> event goes out, the page draws it,
/// and the answer comes back as <c>dialog.respond</c>. Whoever asked waits on the task, which is
/// why a page that goes away must never leave a question hanging — a fresh attach answers every
/// open question "no", the one answer that cannot make things worse.
/// </summary>
public sealed class DialogBridge : IBridgeHandler, IDisposable
{
    private readonly Dictionary<int, TaskCompletionSource<bool>> _waiting = [];

    private BridgeServer? _server;

    private int _nextRequestId;

    /// <summary>
    /// How long the page has to answer. A page that is alive but stuck must not hold a delete or
    /// an exit for ever; the answer on timeout is the same "no" a closed page gives.
    /// </summary>
    internal TimeSpan AnswerTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public void Register(BridgeServer server)
    {
        _server = server;
        server.Register("dialog.respond", Respond);
        server.PageReset += Reset;
    }

    public void Dispose() => _server?.PageReset -= Reset;

    /// <summary>
    /// Asks the page one question and answers with what the person chose. <paramref name="cancelLabel"/>
    /// of null draws a single button: this is a report, not a choice.
    /// </summary>
    public async Task<bool> ConfirmAsync(string title, string message, string confirmLabel, string? cancelLabel)
    {
        if (_server is null || !_server.IsReady)
        {
            // There is no page to draw the question. Buffering it would leave the caller waiting
            // for an answer nobody can give.
            return false;
        }

        var id = _nextRequestId++;
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _waiting[id] = answer;
        await _server.PushAsync("dialog.request",
            new DialogRequest(id, title, message, confirmLabel, cancelLabel));

        var completed = await Task.WhenAny(answer.Task, Task.Delay(AnswerTimeout));
        if (completed != answer.Task)
        {
            _waiting.Remove(id);
            return false;
        }

        return await answer.Task;
    }

    private Task<object?> Respond(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("requestId", out var named) || !named.TryGetInt32(out var id))
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.DialogNeedsRequestId"));
        }

        var accepted = parameters.TryGetProperty("accepted", out var given) && given.ValueKind == JsonValueKind.True;
        if (_waiting.Remove(id, out var answer))
        {
            answer.SetResult(accepted);
        }

        return Task.FromResult<object?>(null);
    }

    /// <summary>A page that went away answers every open question "no".</summary>
    private void Reset()
    {
        foreach (var answer in _waiting.Values)
        {
            answer.SetResult(false);
        }

        _waiting.Clear();
    }
}
