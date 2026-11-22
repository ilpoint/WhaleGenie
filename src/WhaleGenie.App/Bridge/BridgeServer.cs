using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Localization;
using WhaleGenie.Runtime;

namespace WhaleGenie.Bridge;

/// <summary>
/// The pipe between the web interface and the program. Messages from the page come in as one JSON
/// envelope at a time and are answered with one; anything the program wants to say without being
/// asked goes out as an event.
/// </summary>
/// <remarks>
/// The server knows nothing about the web control: a channel hands it text and takes text back,
/// which is what keeps the routing testable without a WebView. Handlers run on the interface
/// thread — the control raises its events there — so they may touch windows and themes freely.
/// Pushes may also come from the trigger thread; they go through one locked queue and are sent in
/// order by a single pump.
/// </remarks>
public sealed class BridgeServer
{
    /// <summary>
    /// Serialization for every envelope. Property names are camelCase; a null is written rather
    /// than omitted, so every field the generated TypeScript interface declares is actually on the
    /// wire. The default encoder escapes non-ASCII and every character a script literal cannot
    /// carry, because the transport hands payloads over as JavaScript expressions rather than strings.
    /// </summary>
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly JsonDocument EmptyParameters = JsonDocument.Parse("{}");

    private readonly Dictionary<string, Func<JsonElement, Task<object?>>> _methods = [];

    /// <summary>The method names the handlers registered, for the inventory check.</summary>
    internal IReadOnlyCollection<string> Methods => _methods.Keys.Order(StringComparer.Ordinal).ToArray();

    /// <summary>Guards every surface's pending buffer, ready queue and ready flag.</summary>
    private readonly object _stateGate = new();

    /// <summary>Serializes sends so two events cannot reach one page in the opposite order.</summary>
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    /// <summary>
    /// One page each, not one page in total: the shell talks about the project, and an overlay
    /// talks about the menu, the wheel or one panel. A push names its surface, and a surface that
    /// has not registered its receiver yet keeps its own buffer instead of delaying the others.
    /// </summary>
    private readonly Dictionary<BridgeSurface, SurfaceState> _surfaces = new()
    {
        [BridgeSurface.Main] = new SurfaceState(),
        [BridgeSurface.Menu] = new SurfaceState(),
        [BridgeSurface.Wheel] = new SurfaceState(),
        [BridgeSurface.Panel] = new SurfaceState(),
    };

    private sealed class PendingPush(string @event, string json)
    {
        public string Event { get; } = @event;

        public string Json { get; set; } = json;

        public TaskCompletionSource Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class SurfaceState
    {
        /// <summary>Event envelopes held back until this page asked for them, one per event.</summary>
        public List<(string Event, string Json)> Pending { get; } = [];

        /// <summary>Events ready to go out; one pump sends them in order.</summary>
        public List<PendingPush> Queue { get; } = [];

        public IBridgeChannel? Channel { get; set; }

        public Action<string>? Pump { get; set; }

        public bool Ready { get; set; }

        public bool Draining { get; set; }
    }

    private SurfaceState State(BridgeSurface surface) => _surfaces[surface];

    /// <summary>
    /// Raised when the server goes back to "no page yet" — a fresh attach or an explicit reset.
    /// A question the old page was answering is dead with it, and whoever asked has to be told
    /// the answer will never come.
    /// </summary>
    public event Action? PageReset;

    /// <summary>
    /// Wires the server to a channel. A page that has just loaded has not registered its receiver
    /// yet, so attaching clears the ready state: pushes buffer again until the new page is ready.
    /// Attaching twice — the shell does it once in its constructor and once per fresh page — must
    /// not listen twice, or every request would be answered twice.
    /// </summary>
    public void Attach(IBridgeChannel channel) => Attach(BridgeSurface.Main, channel);

    /// <summary>The same, for one of the overlay pages beside the shell.</summary>
    public void Attach(BridgeSurface surface, IBridgeChannel channel)
    {
        var state = State(surface);
        if (state.Channel is { } previous && state.Pump is { } pump)
        {
            previous.MessageFromWeb -= pump;
        }

        state.Channel = channel;
        state.Pump = message => _ = AcceptAsync(surface, message);
        state.Channel.MessageFromWeb += state.Pump;
        lock (_stateGate)
        {
            state.Ready = false;
            state.Pending.Clear();
            foreach (var queued in state.Queue)
            {
                queued.Completion.TrySetResult();
            }

            state.Queue.Clear();
        }

        // A dialog question is asked on the shell's page. An overlay reloading is not the page
        // going away, so the questions the shell is answering stay open.
        if (surface is BridgeSurface.Main)
        {
            PageReset?.Invoke();
        }
    }

    /// <summary>Registers every method a handler answers.</summary>
    public void Register(IBridgeHandler handler)
        => handler.Register(this);

    /// <summary>Teaches the server one method. A second registration of the same name is a bug.</summary>
    public void Register(string method, Func<JsonElement, Task<object?>> handler)
    {
        if (_methods.ContainsKey(method))
        {
            throw new InvalidOperationException($"the bridge method {method} is registered twice");
        }

        _methods.Add(method, handler);
    }

    /// <summary>
    /// Answers one envelope from the page. Unparseable text is dropped: it has no id to correlate
    /// an answer with, and the only way it happens is a broken page, which the debugger sees
    /// directly.
    /// </summary>
    public Task AcceptAsync(string json) => AcceptAsync(BridgeSurface.Main, json);

    /// <summary>The same, with the page that spoke named so the answer goes back to it.</summary>
    public async Task AcceptAsync(BridgeSurface surface, string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return;
        }

        using (document)
        {
            var request = document.RootElement;
            var method = request.TryGetProperty("method", out var named) ? named.GetString() : null;
            if (method is null)
            {
                return;
            }

            var id = request.TryGetProperty("id", out var numbered) && numbered.TryGetInt32(out var value)
                ? value
                : (int?)null;

            if (method == BridgeProtocol.ReadyMethod)
            {
                await AnswerReadyAsync(surface, id);
                return;
            }

            if (!_methods.TryGetValue(method, out var handler))
            {
                await AnswerFailureAsync(surface, id, BridgeErrors.NoSuchMethod,
                    Strings.Format("Bridge.NoSuchMethod", method));
                return;
            }

            var parameters = request.TryGetProperty("params", out var given)
                ? given.Clone()
                : EmptyParameters.RootElement.Clone();
            object? result;
            try
            {
                result = await handler(parameters);
            }
            catch (BridgeFailureException failure)
            {
                await AnswerFailureAsync(surface, id, failure.Code, failure.Message);
                return;
            }
            catch (Exception error)
            {
                AppDiagnostics.Record("bridge.method", error);
                await AnswerFailureAsync(surface, id, BridgeErrors.Internal, Strings.Get("Bridge.Internal"));
                return;
            }

            await SendAsync(surface, JsonSerializer.Serialize(new { id, ok = true, result }, Options));
        }
    }

    /// <summary>
    /// Sends one event to the page. Before the page is ready — or before any page is attached —
    /// events buffer, so a theme or language change made while the window loads is not lost.
    /// </summary>
    /// <remarks>
    /// A state event is coalesced: while an earlier push of the same event is still queued, the
    /// newer payload replaces it. The page only needs the latest theme or project state, and four
    /// pushes a second would otherwise queue up behind a slow script call. Streaming events — log
    /// lines, for example — pass <see cref="BridgePushMode.All"/> and keep every push.
    /// </remarks>
    public Task PushAsync(string @event, object? payload,
        BridgePushMode mode = BridgePushMode.Latest, BridgeSurface surface = BridgeSurface.Main)
    {
        var state = State(surface);
        var json = JsonSerializer.Serialize(new { @event, payload }, Options);
        Task completion;
        var start = false;
        lock (_stateGate)
        {
            if (!state.Ready || state.Channel is null)
            {
                state.Pending.RemoveAll(entry => string.Equals(entry.Event, @event, StringComparison.Ordinal));
                state.Pending.Add((@event, json));
                return Task.CompletedTask;
            }

            if (mode == BridgePushMode.Latest)
            {
                var existing = state.Queue.FindLast(entry =>
                    string.Equals(entry.Event, @event, StringComparison.Ordinal));
                if (existing is not null)
                {
                    existing.Json = json;
                    return existing.Completion.Task;
                }
            }

            var queued = new PendingPush(@event, json);
            state.Queue.Add(queued);
            completion = queued.Completion.Task;
            start = !state.Draining;
            state.Draining = start;
        }

        if (start)
        {
            _ = DrainAsync(surface);
        }

        return completion;
    }

    /// <summary>Whether the page has registered its receiver and may be sent events.</summary>
    public bool IsReady => IsReadyOn(BridgeSurface.Main);

    /// <summary>Whether one page has registered its receiver and may be sent events.</summary>
    public bool IsReadyOn(BridgeSurface surface)
    {
        lock (_stateGate)
        {
            return State(surface).Ready;
        }
    }

    /// <summary>
    /// Drops an event waiting for the page. A handler that has just answered the same state in a
    /// request clears the buffered copy, so an older push cannot overwrite the newer answer.
    /// </summary>
    public void DropPending(string @event, BridgeSurface surface = BridgeSurface.Main)
    {
        lock (_stateGate)
        {
            State(surface).Pending.RemoveAll(entry =>
                string.Equals(entry.Event, @event, StringComparison.Ordinal));
        }
    }

    /// <summary>Sends the ready queue in order until it is empty.</summary>
    private async Task DrainAsync(BridgeSurface surface)
    {
        var state = State(surface);
        while (true)
        {
            PendingPush next;
            lock (_stateGate)
            {
                if (state.Queue.Count == 0)
                {
                    state.Draining = false;
                    return;
                }

                next = state.Queue[0];
                state.Queue.RemoveAt(0);
            }

            await SendAsync(surface, next.Json);
            next.Completion.TrySetResult();
        }
    }

    private async Task AnswerReadyAsync(BridgeSurface surface, int? id)
    {
        var state = State(surface);
        (string Event, string Json)[] buffered;
        lock (_stateGate)
        {
            state.Ready = true;
            buffered = [.. state.Pending];
            state.Pending.Clear();
        }

        if (id is { } readyId)
        {
            await SendAsync(surface,
                JsonSerializer.Serialize(new { id = readyId, ok = true, result = (object?)null }, Options));
        }

        foreach (var (_, json) in buffered)
        {
            await SendAsync(surface, json);
        }
    }

    private Task AnswerFailureAsync(BridgeSurface surface, int? id, string code, string message)
        => id is null
            ? Task.CompletedTask
            : SendAsync(surface, JsonSerializer.Serialize(new
            {
                id,
                ok = false,
                error = new { code, message },
            }, Options));

    private async Task SendAsync(BridgeSurface surface, string json)
    {
        await _sendGate.WaitAsync();
        try
        {
            if (State(surface).Channel is not { } channel)
            {
                return;
            }

            await channel.SendToWebAsync(json);
        }
        catch (Exception)
        {
            // A send that fails because the page is navigating or the control is going away
            // loses that one message; the next one after the page is ready is what matters.
        }
        finally
        {
            _sendGate.Release();
        }
    }
}

/// <summary>Which page a message belongs to: the shell, or one of the overlays beside it.</summary>
public enum BridgeSurface
{
    /// <summary>The shell: project, editor, settings and run panels.</summary>
    Main,

    /// <summary>The macro menu overlay.</summary>
    Menu,

    /// <summary>The wheel overlay.</summary>
    Wheel,

    /// <summary>One interface panel beside the window in front.</summary>
    Panel,
}

/// <summary>How a push treats an older push of the same event that has not gone out yet.</summary>
public enum BridgePushMode
{
    /// <summary>Replace the queued payload with the newest one; the page only needs the latest state.</summary>
    Latest,

    /// <summary>Keep every push, in order — log lines and other streams need each one.</summary>
    All,
}

/// <summary>Names the protocol's fixed words in one place.</summary>
public static class BridgeProtocol
{
    /// <summary>The page sends this once its receiver is registered; buffered events follow.</summary>
    public const string ReadyMethod = "bridge.ready";
}

/// <summary>The stable rejection words the bridge answers with.</summary>
public static class BridgeErrors
{
    public const string BadArgument = "BadArgument";
    public const string NoSuchMethod = "NoSuchMethod";
    public const string NoSuchMacro = "NoSuchMacro";
    public const string PackageOpenFailed = "PackageOpenFailed";
    public const string PackageSaveFailed = "PackageSaveFailed";
    public const string Internal = "Internal";
}

/// <summary>A rejection a handler means to hand to the page, with the words to hand it under.</summary>
public sealed class BridgeFailureException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Every handler implements this so the server can register it in one call.</summary>
public interface IBridgeHandler
{
    void Register(BridgeServer server);
}

/// <summary>
/// The one-direction text pipe the server speaks over. The web control adapter implements it for
/// real; the checks stand in with an in-memory pipe.
/// </summary>
public interface IBridgeChannel
{
    /// <summary>Raised with the raw envelope text the page sent.</summary>
    event Action<string>? MessageFromWeb;

    /// <summary>Hands one JSON envelope to the page.</summary>
    Task SendToWebAsync(string json);
}
