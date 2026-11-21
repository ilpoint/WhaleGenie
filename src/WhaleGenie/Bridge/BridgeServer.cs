using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
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
/// which is what keeps the routing testable without a WebView. Everything here runs on the
/// interface thread — the control raises its events there — so handlers may touch windows and
/// themes freely.
/// </remarks>
public sealed class BridgeServer
{
    /// <summary>
    /// Serialization for every envelope. The default encoder escapes non-ASCII and every character
    /// a script literal cannot carry, because the transport hands payloads over as JavaScript
    /// expressions rather than strings.
    /// </summary>
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonDocument EmptyParameters = JsonDocument.Parse("{}");

    private readonly Dictionary<string, Func<JsonElement, Task<object?>>> _methods = [];

    /// <summary>The method names the handlers registered, for the inventory check.</summary>
    internal IReadOnlyCollection<string> Methods => _methods.Keys.Order(StringComparer.Ordinal).ToArray();

    /// <summary>
    /// Event envelopes held back until the page asked for them, one per event: a page that has not
    /// drawn yet wants the latest theme or project, not every state it passed through while loading.
    /// </summary>
    private readonly List<(string Event, string Json)> _pending = [];

    /// <summary>Serializes sends so two events cannot reach the page in the opposite order.</summary>
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    private IBridgeChannel? _channel;

    private Action<string>? _pump;

    private bool _ready;

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
    public void Attach(IBridgeChannel channel)
    {
        if (_channel is { } previous && _pump is { } pump)
        {
            previous.MessageFromWeb -= pump;
        }

        _channel = channel;
        _pump = message => _ = AcceptAsync(message);
        _channel.MessageFromWeb += _pump;
        _ready = false;
        _pending.Clear();
        PageReset?.Invoke();
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
    public async Task AcceptAsync(string json)
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
                await AnswerReadyAsync(id);
                return;
            }

            if (!_methods.TryGetValue(method, out var handler))
            {
                await AnswerFailureAsync(id, BridgeErrors.NoSuchMethod,
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
                await AnswerFailureAsync(id, failure.Code, failure.Message);
                return;
            }
            catch (Exception error)
            {
                AppDiagnostics.Record("bridge.method", error);
                await AnswerFailureAsync(id, BridgeErrors.Internal, Strings.Get("Bridge.Internal"));
                return;
            }

            await SendAsync(JsonSerializer.Serialize(new { id, ok = true, result }, Options));
        }
    }

    /// <summary>
    /// Sends one event to the page. Before the page is ready — or before any page is attached —
    /// events buffer, so a theme or language change made while the window loads is not lost.
    /// </summary>
    public async Task PushAsync(string @event, object? payload)
    {
        var json = JsonSerializer.Serialize(new { @event, payload }, Options);
        if (!_ready || _channel is null)
        {
            _pending.RemoveAll(entry => string.Equals(entry.Event, @event, StringComparison.Ordinal));
            _pending.Add((@event, json));
            return;
        }

        await SendAsync(json);
    }

    /// <summary>Whether the page has registered its receiver and may be sent events.</summary>
    public bool IsReady => _ready;

    /// <summary>
    /// Drops an event waiting for the page. A handler that has just answered the same state in a
    /// request clears the buffered copy, so an older push cannot overwrite the newer answer.
    /// </summary>
    public void DropPending(string @event)
        => _pending.RemoveAll(entry => string.Equals(entry.Event, @event, StringComparison.Ordinal));

    private async Task AnswerReadyAsync(int? id)
    {
        _ready = true;
        if (id is { } readyId)
        {
            await SendAsync(JsonSerializer.Serialize(new { id = readyId, ok = true, result = (object?)null }, Options));
        }

        var buffered = _pending.ToArray();
        _pending.Clear();
        foreach (var (_, json) in buffered)
        {
            await SendAsync(json);
        }
    }

    private Task AnswerFailureAsync(int? id, string code, string message)
        => id is null
            ? Task.CompletedTask
            : SendAsync(JsonSerializer.Serialize(new
            {
                id,
                ok = false,
                error = new { code, message },
            }, Options));

    private async Task SendAsync(string json)
    {
        await _sendGate.WaitAsync();
        try
        {
            if (_channel is null)
            {
                return;
            }

            await _channel.SendToWebAsync(json);
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
