using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Bridge;
using WhaleGenie.Core.Devices;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;

namespace WhaleGenie.Tests;

/// <summary>
/// The macro list's side of the bridge: the state the page draws from, and the two commands the
/// list makes. The trigger service behind it is the real one on a stand-in device layer — arming
/// the system there fails softly, which is what a machine without a hook would do anyway.
/// </summary>
public class ProjectBridgeTests : IDisposable
{
    private readonly ProjectWorkspace _workspace = new();
    private readonly TriggerRuntime _runtime = new(new NullDeviceLayer(), new VariableSession());
    private readonly BridgeServer _server = new();
    private readonly RecordingChannel _channel = new();

    public ProjectBridgeTests()
    {
        _server.Attach(_channel);
        // The window answers null in a check, which is what makes the file dialogs answer
        // "called off" — a package path handed in directly still goes through.
        _server.Register(new ProjectBridge(_workspace, _runtime, () => null));
    }

    public void Dispose() => _runtime.Dispose();

    private static IReadOnlyList<string> Names(JsonElement array)
        => array.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToList();

    [Fact]
    public async Task The_state_carries_the_macros_and_the_views_they_belong_to()
    {
        _workspace.AddMacro(new MacroItem { Name = "自动保存", Group = "办公", Description = "每分钟存一次" });
        _workspace.AddMacro(new MacroItem { Name = "游戏连招" });
        _workspace.Triggers.Entries.Add(new TriggerEntry { MacroName = "游戏连招", Spec = new KeySpec("F8") });

        // A macro with no trigger still belongs to the all view; that is the only view it has.
        await _server.AcceptAsync("""{"id":1,"method":"project.state"}""");

        var answer = JsonDocument.Parse(_channel.Sent.Last(text => text.Contains("\"id\":1"))).RootElement;
        var state = answer.GetProperty("result");
        Assert.Equal(2, state.GetProperty("macros").GetArrayLength());

        var first = state.GetProperty("macros").EnumerateArray().ToList()[0];
        Assert.Equal("自动保存", first.GetProperty("name").GetString());
        Assert.Equal("办公", first.GetProperty("group").GetString());
        Assert.Equal(["All"], Names(first.GetProperty("views")));

        var armed = state.GetProperty("macros").EnumerateArray().ToList()[1];
        var views = Names(armed.GetProperty("views"));
        Assert.Contains("Keys", views);
        Assert.Contains("All", views);
        Assert.Contains("办公", Names(state.GetProperty("groups")));
    }

    [Fact]
    public async Task Toggling_a_macro_flips_it_and_pushes_the_change()
    {
        await _server.AcceptAsync("""{"id":1,"method":"bridge.ready"}""");
        _channel.Sent.Clear();
        _workspace.AddMacro(new MacroItem { Name = "自动保存", IsEnabled = true });

        // Adding a macro already pushes a change, so the answer below is the toggle's own.
        await _server.AcceptAsync("""{"id":2,"method":"project.toggle","params":{"name":"自动保存"}}""");

        Assert.False(_workspace.Macros.Single().IsEnabled);
        var pushed = JsonDocument.Parse(_channel.Sent.Last(text => text.Contains("\"event\":\"project.changed\""))).RootElement;
        Assert.False(pushed.GetProperty("payload").GetProperty("macros").EnumerateArray().Single()
            .GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task An_unknown_macro_is_refused()
    {
        await _server.AcceptAsync("""{"id":2,"method":"project.toggle","params":{"name":"没有的"}}""");

        var answer = JsonDocument.Parse(_channel.Sent.Single(text => text.Contains("\"id\":2"))).RootElement;
        Assert.False(answer.GetProperty("ok").GetBoolean());
        Assert.Equal(BridgeErrors.NoSuchMacro, answer.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_system_switch_arms_the_trigger_service_and_answers()
    {
        await _server.AcceptAsync("""{"id":1,"method":"bridge.ready"}""");
        _channel.Sent.Clear();

        await _server.AcceptAsync("""{"id":3,"method":"project.system","params":{"enabled":true}}""");

        Assert.True(_runtime.Triggers.IsEnabled);
        var pushed = JsonDocument.Parse(_channel.Sent.Single(text => text.Contains("\"event\":\"project.changed\""))).RootElement;
        Assert.True(pushed.GetProperty("payload").GetProperty("systemEnabled").GetBoolean());
    }

    [Fact]
    public async Task An_edit_made_elsewhere_reaches_the_page_as_a_push()
    {
        // A draft applied by MCP, or a package opened, changes the project without the page
        // asking; the change event is how the list stays the same thing the program holds.
        await _server.AcceptAsync("""{"id":4,"method":"bridge.ready"}""");
        _channel.Sent.Clear();

        _workspace.AddMacro(new MacroItem { Name = "MCP 写进来的" });

        Assert.Contains(_channel.Sent, text => text.Contains("\"event\":\"project.changed\""));
    }

    [Fact]
    public async Task Opening_a_package_replaces_the_list_and_names_the_file()
    {
        await _server.AcceptAsync("""{"id":1,"method":"bridge.ready"}""");
        _channel.Sent.Clear();

        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "macros" + MacroPackage.Extension);
        MacroPackage.Save(path, [new MacroItem { Name = "包里的宏" }], new ProjectTriggers(), [], "0.0.0");

        try
        {
            await _server.AcceptAsync(JsonSerializer.Serialize(new
            {
                id = 2,
                method = "project.open",
                @params = new { path },
            }));

            Assert.Equal("包里的宏", _workspace.Macros.Single().Name);
            Assert.Equal(path, _workspace.CurrentPath);
            var pushed = JsonDocument.Parse(_channel.Sent.Last(text => text.Contains("\"event\":\"project.changed\""))).RootElement;
            Assert.Equal("macros", pushed.GetProperty("payload").GetProperty("name").GetString());
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task Saving_writes_the_file_and_clears_the_dirty_flag()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "saved" + MacroPackage.Extension);

        try
        {
            _workspace.AddMacro(new MacroItem { Name = "要保存的" });
            await _server.AcceptAsync("""{"id":1,"method":"bridge.ready"}""");
            _channel.Sent.Clear();

            await _server.AcceptAsync(JsonSerializer.Serialize(new
            {
                id = 2,
                method = "project.save",
                @params = new { path },
            }));

            Assert.True(File.Exists(path));
            Assert.False(_workspace.IsDirty);
            Assert.Equal(path, _workspace.CurrentPath);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task A_broken_package_is_named_to_the_page()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "broken" + MacroPackage.Extension);
        File.WriteAllText(path, "this is not a zip file");

        try
        {
            await _server.AcceptAsync(JsonSerializer.Serialize(new
            {
                id = 1,
                method = "project.open",
                @params = new { path },
            }));

            var answer = JsonDocument.Parse(_channel.Sent.Single(text => text.Contains("\"id\":1"))).RootElement;
            Assert.False(answer.GetProperty("ok").GetBoolean());
            Assert.Equal(BridgeErrors.PackageOpenFailed, answer.GetProperty("error").GetProperty("code").GetString());
            Assert.Contains("broken", answer.GetProperty("error").GetProperty("message").GetString());
            Assert.Empty(_workspace.Macros);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task Reloading_needs_a_file_to_reload_from()
    {
        await _server.AcceptAsync("""{"id":1,"method":"project.reload"}""");

        var answer = JsonDocument.Parse(_channel.Sent.Single(text => text.Contains("\"id\":1"))).RootElement;
        Assert.False(answer.GetProperty("ok").GetBoolean());
        Assert.Equal(BridgeErrors.BadArgument, answer.GetProperty("error").GetProperty("code").GetString());
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
