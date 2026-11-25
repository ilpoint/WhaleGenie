using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Bridge;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Projects;
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
    private readonly SystemState _system = new(false);
    private readonly BridgeServer _server = new();
    private readonly List<UiSound> _sounds = [];
    private readonly RecordingChannel _channel = new();

    private readonly DialogBridge _dialogs;

    public ProjectBridgeTests()
    {
        _server.Attach(_channel);
        // The file dialogs answer "called off" in a check; a package path handed in directly
        // still goes through.
        _dialogs = new DialogBridge();
        _server.Register(_dialogs);
        _server.Register(new ProjectBridge(_workspace, _runtime, _system,
            new NoFileDialogs(), TestProjectFiles.Service, TestProjectFiles.Transfer, _dialogs,
            sound => _sounds.Add(sound), rememberProject: _ => { }));
    }

    public void Dispose() => _runtime.Dispose();

    private sealed class NoFileDialogs : WhaleGenie.Contracts.IFileDialogs
    {
        public Task<string?> OpenPackageAsync() => Task.FromResult<string?>(null);

        public Task<string?> SavePackageAsync(string suggestedName) => Task.FromResult<string?>(null);

        public Task<string?> OpenImageAsync() => Task.FromResult<string?>(null);
    }

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

        // The page sorts the list itself; the program hands it the preference rather than sorting.
        Assert.Equal(LocalSettings.LoadEnabledFirst(),
            state.GetProperty("enabledFirst").GetBoolean());
    }

    [Fact]
    public async Task The_default_project_has_its_own_name_and_reports_the_save_state()
    {
        _workspace.CurrentPath = AppPaths.DefaultProject;
        await _server.AcceptAsync("""{"id":1,"method":"project.state"}""");

        var state = JsonDocument.Parse(_channel.Sent.Last(text => text.Contains("\"id\":1")))
            .RootElement.GetProperty("result");

        Assert.Equal(Strings.Get("Project.DefaultName"), state.GetProperty("name").GetString());
        Assert.False(state.GetProperty("untitled").GetBoolean());
        Assert.True(state.GetProperty("autoSave").GetBoolean());
        Assert.False(state.GetProperty("saving").GetBoolean());
        Assert.Equal(JsonValueKind.Null, state.GetProperty("saveError").ValueKind);
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
    public async Task Toggling_a_macro_plays_the_same_sound_as_the_native_card()
    {
        await _server.AcceptAsync("""{"id":1,"method":"bridge.ready"}""");
        _workspace.AddMacro(new MacroItem { Name = "自动保存", IsEnabled = true });
        _sounds.Clear();

        await _server.AcceptAsync("""{"id":2,"method":"project.toggle","params":{"name":"自动保存"}}""");
        Assert.Equal([UiSound.MacroOff], _sounds);

        await _server.AcceptAsync("""{"id":3,"method":"project.toggle","params":{"name":"自动保存"}}""");
        Assert.Equal([UiSound.MacroOff, UiSound.MacroOn], _sounds);
    }

    [Fact]
    public async Task An_unknown_macro_is_refused()
    {
        await _server.AcceptAsync("""{"id":2,"method":"project.toggle","params":{"name":"没有的"}}""");

        var answer = JsonDocument.Parse(_channel.Sent.Single(text => text.Contains("\"id\":2"))).RootElement;
        Assert.False(answer.GetProperty("ok").GetBoolean());
        Assert.Equal(BridgeErrors.NoSuchMacro, answer.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains("没有", answer.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task The_system_switch_flips_the_state_and_answers()
    {
        await _server.AcceptAsync("""{"id":1,"method":"bridge.ready"}""");
        _channel.Sent.Clear();

        // The switch writes the same object the hotkey and the window write; the trigger host is
        // the one that arms the service and plays the sound.
        await _server.AcceptAsync("""{"id":3,"method":"project.system","params":{"enabled":true}}""");

        Assert.True(_system.IsEnabled);
        var pushed = JsonDocument.Parse(_channel.Sent.Single(text => text.Contains("\"event\":\"project.changed\""))).RootElement;
        Assert.True(pushed.GetProperty("payload").GetProperty("systemEnabled").GetBoolean());
    }

    [Fact]
    public async Task A_switch_flip_from_outside_the_page_is_pushed()
    {
        await _server.AcceptAsync("""{"id":1,"method":"bridge.ready"}""");
        _channel.Sent.Clear();

        // A hotkey or the native window flipped the switch; the page never sent a command, so the
        // only way it learns is the same push every other project change rides on.
        _system.Set(true);

        var pushed = JsonDocument.Parse(_channel.Sent.Last(text => text.Contains("\"event\":\"project.changed\""))).RootElement;
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
    public async Task Closing_everything_asks_the_page_and_waits_for_the_answer()
    {
        _workspace.AddMacro(new MacroItem { Name = "要被清掉的" });
        await _server.AcceptAsync("""{"id":1,"method":"bridge.ready"}""");
        _channel.Sent.Clear();

        // The close waits for the page's answer: take the request without awaiting it, answer,
        // and only then check what it did.
        var asking = _server.AcceptAsync("""{"id":2,"method":"project.closeAll"}""");

        var request = JsonDocument.Parse(_channel.Sent.Single(text => text.Contains("\"event\":\"dialog.request\""))).RootElement;
        Assert.Equal("关闭全部宏", request.GetProperty("payload").GetProperty("title").GetString());

        var requestId = request.GetProperty("payload").GetProperty("requestId").GetInt32();
        await _server.AcceptAsync(
            JsonSerializer.Serialize(new { id = 3, method = "dialog.respond", @params = new { requestId, accepted = true } }));
        await asking;

        Assert.Empty(_workspace.Macros);
        Assert.Null(_workspace.CurrentPath);
    }

    [Fact]
    public async Task A_dismissed_question_changes_nothing()
    {
        _workspace.AddMacro(new MacroItem { Name = "留下来的" });
        await _server.AcceptAsync("""{"id":1,"method":"bridge.ready"}""");
        _channel.Sent.Clear();

        var asking = _server.AcceptAsync("""{"id":2,"method":"project.closeAll"}""");
        var request = JsonDocument.Parse(_channel.Sent.Single(text => text.Contains("\"event\":\"dialog.request\""))).RootElement;
        var requestId = request.GetProperty("payload").GetProperty("requestId").GetInt32();
        await _server.AcceptAsync(
            JsonSerializer.Serialize(new { id = 3, method = "dialog.respond", @params = new { requestId, accepted = false } }));
        await asking;

        Assert.Single(_workspace.Macros);
    }

    [Fact]
    public async Task Deleting_a_macro_stops_its_run_and_removes_it_after_the_confirm()
    {
        _workspace.AddMacro(new MacroItem { Name = "自动保存" });
        await _server.AcceptAsync("""{"id":1,"method":"bridge.ready"}""");
        _channel.Sent.Clear();

        // The delete waits for the page's answer, so the request is taken without awaiting it:
        // the respond is what lets it finish, and only then does the removal land.
        var asking = _server.AcceptAsync("""{"id":2,"method":"project.delete","params":{"name":"自动保存"}}""");

        var request = JsonDocument.Parse(_channel.Sent.Single(text => text.Contains("\"event\":\"dialog.request\""))).RootElement;
        var payload = request.GetProperty("payload");
        Assert.Equal("删除宏", payload.GetProperty("title").GetString());
        Assert.Contains("自动保存", payload.GetProperty("message").GetString(), StringComparison.Ordinal);

        var requestId = payload.GetProperty("requestId").GetInt32();
        await _server.AcceptAsync(
            JsonSerializer.Serialize(new { id = 3, method = "dialog.respond", @params = new { requestId, accepted = true } }));
        await asking;

        Assert.Empty(_workspace.Macros);
    }

    [Fact]
    public async Task The_export_plan_lists_the_projects_macros()
    {
        _workspace.AddMacro(new MacroItem { Name = "自动保存" });
        await _server.AcceptAsync("""{"id":1,"method":"bridge.ready"}""");
        _channel.Sent.Clear();

        await _server.AcceptAsync("""{"id":2,"method":"project.exportPlan"}""");

        var answer = JsonDocument.Parse(_channel.Sent.Single(text => text.Contains("\"id\":2"))).RootElement;
        Assert.True(answer.GetProperty("ok").GetBoolean());
        var plan = answer.GetProperty("result");
        Assert.EndsWith(MacroPackage.Extension,
            plan.GetProperty("suggestedName").GetString()!, StringComparison.Ordinal);
        Assert.Equal("自动保存",
            plan.GetProperty("macros").EnumerateArray().Single().GetProperty("name").GetString());
    }

    [Fact]
    public async Task A_package_plan_and_apply_round_trip_over_the_bridge()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "incoming" + MacroPackage.Extension);
        MacroPackage.Save(path, [new MacroItem { Name = "包里的" }], new ProjectTriggers(), [], "0.0.0");

        try
        {
            await _server.AcceptAsync(JsonSerializer.Serialize(new
            {
                id = 1,
                method = "project.importPlan",
                @params = new { path },
            }));

            var plan = JsonDocument.Parse(_channel.Sent.Single(text => text.Contains("\"id\":1")))
                .RootElement.GetProperty("result");
            Assert.Equal("incoming.wgmacro", plan.GetProperty("fileName").GetString());
            Assert.Equal("包里的",
                plan.GetProperty("macros").EnumerateArray().Single().GetProperty("name").GetString());

            await _server.AcceptAsync(JsonSerializer.Serialize(new
            {
                id = 2,
                method = "project.importApply",
                @params = new { path, names = new[] { "包里的" }, decisions = Array.Empty<object>() },
            }));

            var answer = JsonDocument.Parse(_channel.Sent.Single(text => text.Contains("\"id\":2")))
                .RootElement;
            Assert.True(answer.GetProperty("ok").GetBoolean());
            Assert.Contains("包里的", answer.GetProperty("result").GetProperty("report").GetString()!,
                StringComparison.Ordinal);
            Assert.Equal("包里的", Assert.Single(_workspace.Macros).Name);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task A_fresh_page_answers_every_open_question_no()
    {
        _workspace.AddMacro(new MacroItem { Name = "留下来的" });
        await _server.AcceptAsync("""{"id":1,"method":"bridge.ready"}""");
        _channel.Sent.Clear();

        // Nobody will answer this one: the page is replaced before it could.
        var asking = _server.AcceptAsync("""{"id":2,"method":"project.closeAll"}""");

        // A fresh page re-attaches (which resets the server) and says it is ready.
        _server.Attach(_channel);
        await _server.AcceptAsync("""{"id":3,"method":"bridge.ready"}""");
        await asking;

        Assert.Single(_workspace.Macros);
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
    public async Task A_library_package_is_refused_as_a_project()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "public-macros" + MacroPackage.Extension);
        PublicMacroLibrary.Save(path,
            [new PublicMacroEntry("public-id", 1, new MacroItem { Name = "shared" })], "test");

        try
        {
            await _server.AcceptAsync(JsonSerializer.Serialize(new
            {
                id = 1,
                method = "project.open",
                @params = new { path },
            }));

            var answer = JsonDocument.Parse(_channel.Sent.Single(text => text.Contains("\"id\":1")))
                .RootElement;
            Assert.False(answer.GetProperty("ok").GetBoolean());
            Assert.Equal(BridgeErrors.BadArgument,
                answer.GetProperty("error").GetProperty("code").GetString());
            Assert.Contains("公共宏库", answer.GetProperty("error").GetProperty("message").GetString());
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

    [Fact]
    public async Task A_cancelled_project_change_guard_keeps_the_current_project()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "incoming" + MacroPackage.Extension);
        MacroPackage.Save(path, [new MacroItem { Name = "incoming" }], new ProjectTriggers(), [], "0.0.0");

        try
        {
            var workspace = new ProjectWorkspace();
            workspace.AddMacro(new MacroItem { Name = "current" });
            var server = new BridgeServer();
            var channel = new RecordingChannel();
            server.Attach(channel);
            var guard = new Guard { Answer = false };
            var dialogs = new DialogBridge();
            server.Register(dialogs);
            server.Register(new ProjectBridge(workspace, _runtime, _system,
                new NoFileDialogs(), TestProjectFiles.Service, TestProjectFiles.Transfer, dialogs, rememberProject: _ => { },
                changeGuard: guard));

            await server.AcceptAsync(JsonSerializer.Serialize(new
            {
                id = 1,
                method = "project.open",
                @params = new { path },
            }));

            Assert.Equal(ProjectChangeKind.Open, guard.Kind);
            Assert.Equal("current", workspace.Macros.Single().Name);
            Assert.Null(workspace.CurrentPath);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task One_turn_of_changes_produces_one_project_push()
    {
        var workspace = new ProjectWorkspace();
        var server = new BridgeServer();
        var channel = new RecordingChannel();
        server.Attach(channel);
        var post = new QueuedPost();
        var dialogs = new DialogBridge();
        server.Register(dialogs);
        server.Register(new ProjectBridge(workspace, _runtime, _system,
            new NoFileDialogs(), TestProjectFiles.Service, TestProjectFiles.Transfer, dialogs, rememberProject: _ => { }, ui: post));
        await server.AcceptAsync("""{"id":1,"method":"bridge.ready"}""");
        channel.Sent.Clear();

        workspace.AddMacro(new MacroItem { Name = "one" });
        workspace.AddMacro(new MacroItem { Name = "two" });

        Assert.Single(post.Actions);
        post.RunAll();
        await Task.Delay(50);
        Assert.Single(channel.Sent, text => text.Contains("\"event\":\"project.changed\""));
    }

    private sealed class Guard : IProjectChangeGuard
    {
        public bool Answer { get; init; }

        public ProjectChangeKind Kind { get; private set; }

        public Task<bool> ConfirmAsync(ProjectChangeKind kind)
        {
            Kind = kind;
            return Task.FromResult(Answer);
        }
    }

    private sealed class QueuedPost : IUiPost
    {
        public List<Action> Actions { get; } = [];

        public void Post(Action action) => Actions.Add(action);

        public Task<T> InvokeAsync<T>(Func<Task<T>> work) => work();

        public void RunAll()
        {
            foreach (var action in Actions.ToList())
            {
                action();
            }

            Actions.Clear();
        }
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
