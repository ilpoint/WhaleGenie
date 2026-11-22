using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Bridge;
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
        var bridge = new MenuSurfaceBridge(menu);
        var overlay = new RecordingOverlay();
        bridge.Overlay = overlay;
        bridge.Register(server);

        await server.AcceptAsync("""{"id":1,"method":"surface.menu.close"}""");

        Assert.Equal(1, overlay.Hidden);
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
        var bridge = new MenuSurfaceBridge(Surface(project));
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

        Assert.Equal(1, overlay.Hidden);
    }

    [Fact]
    public async Task An_entry_that_is_gone_does_not_run_anything()
    {
        var server = new BridgeServer();
        var bridge = new MenuSurfaceBridge(Surface());
        var overlay = new RecordingOverlay();
        bridge.Overlay = overlay;
        bridge.Register(server);

        // Unknown id answers "not started" rather than failing the page's click.
        await server.AcceptAsync("""{"id":3,"method":"surface.menu.activate","params":{"entryId":"nope"}}""");
        Assert.False(overlay.IsVisible);
    }

    private static MenuSurface Surface(ProjectWorkspace? project = null)
        => new(project ?? new ProjectWorkspace(), NullDeviceLayer.Instance.Windows,
            new TriggerRuntime(NullDeviceLayer.Instance, (project ?? new ProjectWorkspace()).Variables));

    private sealed class RecordingOverlay : IMenuOverlay
    {
        public bool IsVisible { get; private set; } = true;

        public int Hidden { get; private set; }

        public void Show() => IsVisible = true;

        public void Hide()
        {
            Hidden++;
            IsVisible = false;
        }
    }
}