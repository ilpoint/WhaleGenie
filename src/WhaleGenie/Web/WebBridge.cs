using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using WhaleGenie.Bridge;
using WhaleGenie.Runtime;

namespace WhaleGenie.Web;

/// <summary>
/// The bridge channel over the real web control. Messages from the page arrive through the
/// control's own event; messages to the page are evaluated as a script that hands one JSON
/// expression to the page's receiver.
/// </summary>
/// <remarks>
/// This is the only type in the program that knows both sides of the bridge: the server and its
/// handlers speak JSON envelopes over <see cref="IBridgeChannel"/>, and this adapter knows how a
/// <see cref="NativeWebView"/> moves text in and out.
/// </remarks>
public sealed class WebBridge(NativeWebView webView) : IBridgeChannel
{
    public event Action<string>? MessageFromWeb;

    /// <summary>Subscribes to the page. Called once the constructor is done, in the window.</summary>
    internal void Listen()
        => webView.WebMessageReceived += (_, received) =>
        {
            var body = received.Body as string;
            AppDiagnostics.Note("webshell.message",
                body is null
                    ? received.Body?.GetType().Name ?? "null"
                    : body.Length <= 240 ? body : body[..240] + "…");
            if (body is not null)
            {
                MessageFromWeb?.Invoke(body);
            }
        };

    public async Task SendToWebAsync(string json)
    {
        try
        {
            // JSON is a valid JavaScript expression and the server's serializer escapes every
            // character a script literal cannot carry, so the envelope is handed over as an
            // expression rather than concatenated into a string.
            // A push can start on the trigger thread or in an MCP handler; the control and the
            // page belong to the interface thread.
            if (Dispatcher.UIThread.CheckAccess())
            {
                await webView.InvokeScript($"window.__whale.onMessage({json})");
            }
            else
            {
                await Dispatcher.UIThread.InvokeAsync(
                    () => webView.InvokeScript($"window.__whale.onMessage({json})"));
            }

            AppDiagnostics.Note("webshell.push", json.Length > 60 ? $"{json[..60]}…" : json);
        }
        catch (Exception error)
        {
            // The page can be mid-navigation or already gone when a push is sent; that one
            // message is lost, and the next one after the page is ready is what matters.
            AppDiagnostics.Record("webshell.push.lost", error);
        }
    }
}
