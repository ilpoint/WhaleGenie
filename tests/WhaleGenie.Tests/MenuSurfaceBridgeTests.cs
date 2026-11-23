using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Bridge;
using WhaleGenie.Contracts;
using WhaleGenie.Core.Devices;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// The menu page's two commands: closing it hides the window, and running a macro hides it first so
/// the program the macro is about gets the foreground back.
/// </summary>
public class MenuSurfaceBridgeTests
{
    [Fact]
    public async Task Closing_the_page_hides_the_overlay()
    {
        var server = new BridgeServer();
        var menu = Surface();
        var bridge = new MenuSurfaceBridge(menu, new FakeFiles());
        var overlay = new RecordingOverlay();
        bridge.Overlay = overlay;
        bridge.Register(server);

        await server.AcceptAsync("""{"id":1,"method":"surface.menu.close"}""");

        Assert.Equal(1, overlay.HideCount);
        Assert.False(overlay.IsVisible);
    }

    [Fact]
    public async Task Running_a_cell_hides_the_overlay_before_the_macro_starts()
    {
        var project = new ProjectWorkspace();
        project.AddMacro(new MacroItem { Name = "one" });
        var page = project.Triggers.CommonMenuPages()[0];
        var entry = new TriggerEntry { MacroName = "one", Spec = new MenuSpec(page.Id, 0, 0) };
        project.Triggers.Entries.Add(entry);

        var server = new BridgeServer();
        var bridge = new MenuSurfaceBridge(Surface(project), new FakeFiles());
        var overlay = new RecordingOverlay();
        bridge.Overlay = overlay;
        bridge.Register(server);

        var request = JsonSerializer.Serialize(new
        {
            id = 2,
            method = "surface.menu.activate",
            @params = new { entryId = entry.Id },
        });
        await server.AcceptAsync(request);

        Assert.Equal(1, overlay.HideCount);
    }

    [Fact]
    public async Task An_entry_that_is_gone_does_not_run_anything()
    {
        var server = new BridgeServer();
        var bridge = new MenuSurfaceBridge(Surface(), new FakeFiles());
        var overlay = new RecordingOverlay();
        bridge.Overlay = overlay;
        bridge.Register(server);

        // Unknown id answers "not started" rather than failing the page's click.
        await server.AcceptAsync("""{"id":3,"method":"surface.menu.activate","params":{"entryId":"nope"}}""");
        Assert.False(overlay.IsVisible);
    }

    [Fact]
    public async Task Pinning_the_menu_is_kept_on_the_overlay()
    {
        var server = new BridgeServer();
        var bridge = new MenuSurfaceBridge(Surface(), new FakeFiles());
        var overlay = new RecordingOverlay();
        bridge.Overlay = overlay;
        bridge.Register(server);

        await server.AcceptAsync("""{"id":4,"method":"surface.menu.pin","params":{"pinned":true}}""");
        Assert.True(overlay.Pinned);

        await server.AcceptAsync("""{"id":5,"method":"surface.menu.pin","params":{"pinned":false}}""");
        Assert.False(overlay.Pinned);
    }

    [Fact]
    public async Task Assigning_and_setting_an_icon_write_the_project()
    {
        var project = new ProjectWorkspace();
        project.AddMacro(new MacroItem { Name = "one" });
        var page = project.Triggers.CommonMenuPages()[0];
        var server = new BridgeServer();
        var bridge = new MenuSurfaceBridge(Surface(project), new FakeFiles());
        bridge.Register(server);

        await server.AcceptAsync(JsonSerializer.Serialize(new
        {
            id = 10,
            method = "surface.menu.cell.assign",
            @params = new { pageId = page.Id, row = 1, column = 2, name = "one" },
        }));
        var entry = Assert.Single(project.Triggers.Entries);
        Assert.Equal("one", entry.MacroName);

        await server.AcceptAsync(JsonSerializer.Serialize(new
        {
            id = 11,
            method = "surface.menu.cell.icon",
            @params = new { entryId = entry.Id, kind = "builtin", value = "Play" },
        }));
        var cell = Assert.Single(bridge.Menu.Snapshot().Common.SelectMany(candidate => candidate.Cells));
        Assert.Equal("builtin", cell.IconKind);
        Assert.Equal("Play", cell.IconValue);
    }

    [Fact]
    public async Task The_image_command_asks_the_file_dialogs()
    {
        var files = new FakeFiles { ImagePath = @"C:\icons\one.png" };
        var server = new BridgeServer();
        new MenuSurfaceBridge(Surface(), files).Register(server);

        await server.AcceptAsync("""{"id":12,"method":"surface.menu.image"}""");

        Assert.True(files.ImageAsked);
    }

    private static MenuSurface Surface(ProjectWorkspace? project = null)
        => new(project ?? new ProjectWorkspace(), NullDeviceLayer.Instance.Windows,
            new TriggerRuntime(NullDeviceLayer.Instance, (project ?? new ProjectWorkspace()).Variables));

    private sealed class FakeFiles : IFileDialogs
    {
        public string? ImagePath { get; init; }

        public bool ImageAsked { get; private set; }

        public Task<string?> OpenImageAsync()
        {
            ImageAsked = true;
            return Task.FromResult(ImagePath);
        }

        public Task<string?> OpenPackageAsync() => Task.FromResult<string?>(null);

        public Task<string?> SavePackageAsync(string suggestedName) => Task.FromResult<string?>(null);
    }

    private sealed class RecordingOverlay : IMenuOverlay
    {
        public bool IsVisible { get; private set; } = true;

        public bool Pinned { get; set; }

        public bool AutoCloseSuspended { get; set; }

        public void MouseDown(OverlayPoint point)
        {
        }

        public event Action? Hidden;

        public event Action<bool>? FocusChanged;

        public bool SearchFocused { get; private set; }

        public void SetSearchFocused(bool focused)
        {
            SearchFocused = focused;
            FocusChanged?.Invoke(focused);
        }

        public int HideCount { get; private set; }

        public void Warm()
        {
        }

        public void BeginDrag()
        {
        }

        public void Show() => IsVisible = true;

        public void Hide()
        {
            HideCount++;
            Hidden?.Invoke();
            IsVisible = false;
        }
    }
}