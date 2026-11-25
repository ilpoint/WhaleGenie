using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Bridge;
using WhaleGenie.Localization;
using WhaleGenie.Runtime;

namespace WhaleGenie.Tests;

/// <summary>
/// The switch and its key as the side bar reads them. The page asks once at startup and then
/// follows the push, so the push has to carry the new key as well as the new on/off state.
/// </summary>
public class SystemBridgeTests
{
    [Fact]
    public async Task The_state_carries_the_switch_and_the_key()
    {
        var folder = Folder();
        try
        {
            var settings = new AppSettings(Path.Combine(folder, "settings.json"));
            settings.StoreSystemHotkey("Ctrl+F12");
            var system = new SystemState(false);
            var server = new BridgeServer();
            var channel = new Channel();
            server.Attach(channel);
            server.Register(new SystemBridge(system, settings));

            await server.AcceptAsync("""{"id":1,"method":"system.state"}""");

            var result = Answer(channel, 1).GetProperty("result");
            Assert.False(result.GetProperty("enabled").GetBoolean());
            Assert.Equal("Ctrl+F12", result.GetProperty("hotkeyDisplay").GetString());
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task Flipping_the_switch_or_the_key_pushes_the_new_state()
    {
        var folder = Folder();
        try
        {
            var settings = new AppSettings(Path.Combine(folder, "settings.json"));
            settings.StoreSystemHotkey("Home");
            var system = new SystemState(false);
            var server = new BridgeServer();
            var channel = new Channel();
            server.Attach(channel);
            var bridge = new SystemBridge(system, settings);
            server.Register(bridge);
            await server.AcceptAsync("""{"id":1,"method":"bridge.ready"}""");
            channel.Sent.Clear();

            system.Set(true);
            var pushed = await Until(channel, "system.changed");
            Assert.True(pushed.GetProperty("payload").GetProperty("enabled").GetBoolean());

            channel.Sent.Clear();
            settings.StoreSystemHotkey("Ctrl+K+L");
            bridge.Publish();
            var keyed = await Until(channel, "system.changed");
            Assert.Equal("Ctrl+K+L",
                keyed.GetProperty("payload").GetProperty("hotkeyDisplay").GetString());
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    private static JsonElement Answer(Channel channel, int id)
    {
        foreach (var text in channel.Sent)
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.TryGetProperty("id", out var answer)
                && answer.GetInt32() == id)
            {
                return document.RootElement.Clone();
            }
        }

        throw new InvalidOperationException("no answer for request " + id);
    }

    private static async Task<JsonElement> Until(Channel channel, string @event)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            foreach (var text in channel.Sent)
            {
                using var document = JsonDocument.Parse(text);
                if (document.RootElement.TryGetProperty("event", out var name)
                    && name.GetString() == @event)
                {
                    return document.RootElement.Clone();
                }
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("the bridge did not push " + @event);
    }

    private static string Folder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-system-"
            + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private sealed class Channel : IBridgeChannel
    {
        public List<string> Sent { get; } = [];

        public event Action<string>? MessageFromWeb;

        public Task SendToWebAsync(string json)
        {
            lock (Sent)
            {
                Sent.Add(json);
            }

            return Task.CompletedTask;
        }

        public void Receive(string json) => MessageFromWeb?.Invoke(json);
    }
}
