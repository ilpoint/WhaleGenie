using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Bridge;
using WhaleGenie.Localization;

namespace WhaleGenie.Tests;

/// <summary>
/// The bundle the web interface draws every word from. It is not a copy of the tables: it is
/// every key of every table resolved for the current language, so the page never guesses which
/// table a key belongs to.
/// </summary>
public class StringsBundleTests
{
    [Fact]
    public void The_snapshot_carries_every_key_of_every_table()
    {
        var snapshot = Strings.Snapshot();

        var expected = Strings.English.Keys
            .Concat(Strings.Chinese.Keys)
            .Concat(ActionStrings.Chinese.Keys)
            .Concat(ActionEnglish.English.Keys)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(expected, snapshot.Keys.Order(StringComparer.Ordinal).ToList());
    }

    [Fact]
    public void Every_entry_of_the_snapshot_is_what_the_program_would_say()
    {
        var snapshot = Strings.Snapshot();

        foreach (var (key, value) in snapshot)
        {
            Assert.False(string.IsNullOrEmpty(value), $"the bundle carries an empty text for {key}");
            Assert.Equal(Strings.Get(key), value);
        }
    }

    [Fact]
    public void The_bridge_hands_the_bundle_over_and_pushes_the_next_language()
    {
        // Switching the language is what the whole interface listens to, so it happens on the
        // interface thread, like every other change a user makes.
        Ui.RunAsync<object?>(async () =>
        {
            var channel = new RecordingChannel();
            var server = new BridgeServer();
            server.Attach(channel);
            server.Register(new StringsBridge());
            var language = Strings.Current.Language;

            try
            {
                // The page asks for the bundle once it is ready; the answer carries the words the
                // program itself would have said, keyed the same way.
                await server.AcceptAsync("""{"id":1,"method":"bridge.ready"}""");
                await server.AcceptAsync("""{"id":2,"method":"strings.bundle"}""");

                var answer = JsonDocument.Parse(channel.Sent.Single(text => text.Contains("\"id\":2"))).RootElement;
                Assert.Equal(2, answer.GetProperty("id").GetInt32());
                var entries = answer.GetProperty("result").GetProperty("entries");
                Assert.False(string.IsNullOrWhiteSpace(entries.GetProperty("Shell.Home").GetString()));

                // Switching the language is a push, not a poll: the page that is already up hears
                // about the new bundle without asking again.
                channel.Sent.Clear();
                Strings.Current.Language = Language.English;

                var pushed = JsonDocument.Parse(channel.Sent.Single()).RootElement;
                Assert.Equal("strings.changed", pushed.GetProperty("event").GetString());
                Assert.Equal("en", pushed.GetProperty("payload").GetProperty("language").GetString());
            }
            finally
            {
                Strings.Current.Language = language;
            }

            return null;
        });
    }

    private sealed class RecordingChannel : IBridgeChannel
    {
        public List<string> Sent { get; } = [];

        public event Action<string>? MessageFromWeb;

        public Task SendToWebAsync(string json)
        {
            Sent.Add(json);
            return Task.CompletedTask;
        }

        public void Receive(string json) => MessageFromWeb?.Invoke(json);
    }
}
