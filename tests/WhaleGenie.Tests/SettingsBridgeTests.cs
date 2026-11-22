using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Bridge;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Tests;

/// <summary>
/// The settings page talks to one bridge: the menu rules and the wheel action manager are stored
/// by the same C# model the desktop wheel reads, not by a second settings table in the page.
/// </summary>
public class SettingsBridgeTests
{
    [Fact]
    public async Task The_menu_launcher_is_validated_and_saved_through_the_bridge()
    {
        using var files = new TempSettings();
        var project = new MainViewModel();
        var changed = 0;
        var server = Server(project, () => changed++);
        var channel = Channel(server);

        var answer = await Call(server, channel, "settings.menu.capture",
            new { binding = "Mouse Middle" });
        var menu = answer.GetProperty("menu");
        Assert.Equal("Mouse Middle", menu.GetProperty("launcher").GetString());
        Assert.Equal("Mouse Middle", LocalSettings.LoadMenuLauncher());
        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task A_settings_patch_writes_only_the_field_it_carries()
    {
        using var files = new TempSettings();
        LocalSettings.StoreEnabledFirst(false);
        LocalSettings.StoreLogCount(100);
        var project = new MainViewModel();
        var changed = 0;
        var server = Server(project, () => changed++);
        var channel = Channel(server);

        var answer = await Call(server, channel, "settings.update",
            new { patch = new { enabledFirst = true } });

        Assert.True(answer.GetProperty("enabledFirst").GetBoolean());
        Assert.Equal(100, answer.GetProperty("logCount").GetInt32());
        Assert.True(LocalSettings.LoadEnabledFirst());
        Assert.Equal(100, LocalSettings.LoadLogCount());
        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task An_empty_settings_patch_is_refused_and_changes_nothing()
    {
        using var files = new TempSettings();
        LocalSettings.StoreEnabledFirst(false);
        var server = Server(new MainViewModel());
        var channel = Channel(server);

        var error = await CallError(server, channel, "settings.update", new { patch = new { } });

        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("code").GetString()));
        Assert.False(LocalSettings.LoadEnabledFirst());
    }

    [Fact]
    public async Task A_window_rule_added_by_the_page_builds_the_same_menu_pages_the_editor_did()
    {
        using var files = new TempSettings();
        var project = new MainViewModel();
        var server = Server(project);
        var channel = Channel(server);

        var answer = await Call(server, channel, "settings.menu.rule.add", new
        {
            rule = new
            {
                title = "Notepad",
                process = "notepad",
                className = "Notepad",
                handle = 0,
                how = WindowHow.TitleContains,
                caseSensitive = false,
                trim = true,
                multiline = false,
            },
        });
        var row = answer.GetProperty("menu").GetProperty("rules").EnumerateArray()
            .Single(rule => rule.GetProperty("summary").GetString()!.Contains("Notepad"));
        Assert.False(row.GetProperty("isDefault").GetBoolean());

        var page = Assert.Single(project.Triggers.ContextMenuPages());
        Assert.Equal("Notepad", page.Window.Title);
    }

    [Fact]
    public async Task A_wheel_slot_written_by_the_page_lands_in_the_projects_scene()
    {
        using var files = new TempSettings();
        var project = new MainViewModel();
        var scene = project.Triggers.Wheel.AddScene(
            new WindowRule(Process: "notepad", How: WindowHow.ProcessEquals));
        var server = Server(project);
        var channel = Channel(server);

        await Call(server, channel, "settings.wheel.scene.select", new { key = scene.Key });
        var answer = await Call(server, channel, "settings.wheel.slot.set", new
        {
            ring = "inner",
            index = 0,
            macro = "复制",
            note = "复制文字",
            icon = (object?)null,
        });

        var slot = scene.At(new WheelSlotRef(WheelRing.Inner, 0));
        Assert.Equal("复制", slot.Macro);
        Assert.Equal("复制文字", slot.Note);
        var written = answer.GetProperty("wheel").GetProperty("slots").EnumerateArray()
            .Single(item => item.GetProperty("ring").GetString() == "inner"
                            && item.GetProperty("index").GetInt32() == 0);
        Assert.Equal("override", written.GetProperty("tone").GetString());
    }

    [Fact]
    public async Task A_wheel_size_is_clamped_and_saved_by_the_model()
    {
        using var files = new TempSettings();
        var project = new MainViewModel();
        var server = Server(project);
        var channel = Channel(server);

        await Call(server, channel, "settings.wheel.update", new
        {
            field = "diameter",
            value = "900",
        });

        Assert.Equal(WheelOptions.LargestDiameter, WheelMenuStore.Load().Options.Diameter);
    }

    [Fact]
    public async Task The_theme_bridge_reports_a_refused_accent_and_the_fallback_it_paints()
    {
        using var files = new TempSettings();
        var original = ThemeController.Load();
        try
        {
            ThemeController.Save(new ThemeSelection(ThemeMode.Custom, false, "#FFFFFF"));
            var server = new BridgeServer();
            server.Register(new ThemeBridge(new WhaleGenie.Web.ThemeCommands()));
            var channel = Channel(server);

            var state = await Call(server, channel, "theme.get");
            Assert.False(state.GetProperty("accentUsable").GetBoolean());
            Assert.Equal(ThemePalette.DefaultAccent, state.GetProperty("fill").GetString());
            Assert.Equal(ThemePalette.DefaultAccent, state.GetProperty("defaultAccent").GetString());
        }
        finally
        {
            ThemeController.Save(original);
        }
    }

    private static BridgeServer Server(MainViewModel project)
        => Server(project, () => { });

    private static BridgeServer Server(MainViewModel project, Action changed)
    {
        var server = new BridgeServer();
        server.Register(new SettingsBridge(
            new SettingsViewModel(() => DriverInputState.Ready, project: project.Workspace,
                markMenuChanged: project.MarkMenuChanged, system: project.SystemSwitch),
            project.Workspace, WhaleGenie.Runtime.SharedDevices.Current,
            new NoSettingsPickers(), changed));
        return server;
    }

    private sealed class NoSettingsPickers : WhaleGenie.Bridge.ISettingsPickerHost
    {
        public Task<WhaleGenie.Models.MenuIconReference?> PickMenuIconAsync()
            => Task.FromResult<WhaleGenie.Models.MenuIconReference?>(null);

        public Task<WhaleGenie.Core.Devices.WindowInfo?> PickWindowAsync()
            => Task.FromResult<WhaleGenie.Core.Devices.WindowInfo?>(null);
    }

    private static RecordingChannel Channel(BridgeServer server)
    {
        var channel = new RecordingChannel();
        server.Attach(channel);
        return channel;
    }

    private static async Task<JsonElement> Call(BridgeServer server, RecordingChannel channel,
        string method, object? parameters = null)
    {
        channel.Sent.Clear();
        await server.AcceptAsync(JsonSerializer.Serialize(new { id = 1, method, @params = parameters }));
        var answer = JsonDocument.Parse(Assert.Single(channel.Sent)).RootElement.Clone();
        Assert.True(answer.GetProperty("ok").GetBoolean());
        return answer.GetProperty("result");
    }

    private static async Task<JsonElement> CallError(BridgeServer server, RecordingChannel channel,
        string method, object? parameters = null)
    {
        channel.Sent.Clear();
        await server.AcceptAsync(JsonSerializer.Serialize(new { id = 1, method, @params = parameters }));
        var answer = JsonDocument.Parse(Assert.Single(channel.Sent)).RootElement.Clone();
        Assert.False(answer.GetProperty("ok").GetBoolean());
        return answer.GetProperty("error");
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

    /// <summary>A settings and wheel file per check, restored before the next one sees them.</summary>
    private sealed class TempSettings : IDisposable
    {
        private readonly string _folder;
        private readonly string? _settings;
        private readonly string? _wheel;

        public TempSettings()
        {
            _folder = Path.Combine(Path.GetTempPath(),
                "whalegenie-bridge-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
            _settings = LocalSettings.FilePath;
            _wheel = WheelMenuStore.FilePathOverride;
            LocalSettings.FilePath = Path.Combine(_folder, "settings.json");
            WheelMenuStore.FilePathOverride = Path.Combine(_folder, "wheel-menu.json");
        }

        public void Dispose()
        {
            LocalSettings.FilePath = _settings!;
            WheelMenuStore.FilePathOverride = _wheel;
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, true);
            }
        }
    }
}
