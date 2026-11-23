using System;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Contracts;
using WhaleGenie.Localization;
using WhaleGenie.Runtime;

namespace WhaleGenie.Bridge;

/// <summary>
/// The menu's side of the bridge: the pages the project has, the cells they put macros in, and the
/// macros and icons the page's own pickers offer. Paging and search are the page's state.
/// </summary>
internal sealed class MenuSurfaceBridge(MenuSurface menu, IFileDialogs files) : IBridgeHandler
{
    /// <summary>The projection itself, so the shell can hand it the interface behind the page.</summary>
    internal MenuSurface Menu => menu;

    /// <summary>The window the page speaks through, once the shell has built it.</summary>
    internal IMenuOverlay? Overlay { get; set; }

    public void Register(BridgeServer server)
    {
        server.Register("surface.menu.state", _ => Task.FromResult<object?>(State()));
        server.Register("surface.menu.activate", Activate);
        server.Register("surface.menu.drag", _ =>
        {
            Overlay?.BeginDrag();
            return Task.FromResult<object?>(null);
        });
        server.Register("surface.menu.close", _ =>
        {
            Overlay?.Hide();
            return Task.FromResult<object?>(null);
        });
        server.Register("surface.menu.focus", parameters =>
        {
            var focused = parameters.TryGetProperty("focused", out var given)
                && given.ValueKind == JsonValueKind.True;
            Overlay?.SetSearchFocused(focused);
            return Task.FromResult<object?>(null);
        });
        server.Register("surface.menu.settings", _ =>
        {
            Overlay?.Hide();
            menu.Actions?.OpenSettings();
            return Task.FromResult<object?>(null);
        });
        server.Register("surface.menu.pin", parameters =>
        {
            var pinned = parameters.TryGetProperty("pinned", out var given)
                && given.ValueKind == JsonValueKind.True;
            if (Overlay is { } overlay)
            {
                overlay.Pinned = pinned;
            }

            return Task.FromResult<object?>(new { pinned });
        });
        server.Register("surface.menu.edit", parameters =>
            Task.FromResult<object?>(new { opened = menu.Edit(RequiredString(parameters, "entryId")) }));
        server.Register("surface.menu.debug", parameters =>
            Task.FromResult<object?>(new { opened = menu.Debug(RequiredString(parameters, "entryId")) }));
        server.Register("surface.menu.macros", _ =>
            Task.FromResult<object?>(menu.MacroChoices()));
        server.Register("surface.menu.cell.assign", parameters =>
            Task.FromResult<object?>(menu.AssignCell(
                RequiredString(parameters, "pageId"),
                RequiredInt(parameters, "row"),
                RequiredInt(parameters, "column"),
                RequiredString(parameters, "name"))));
        server.Register("surface.menu.cell.icon", parameters =>
            Task.FromResult<object?>(menu.SetIcon(
                RequiredString(parameters, "entryId"),
                RequiredString(parameters, "kind"),
                RequiredString(parameters, "value"))));
        server.Register("surface.menu.image", _ => Suspended(async () =>
            new { path = await files.OpenImageAsync() }));
        server.Register("surface.menu.page.add", parameters =>
            Task.FromResult<object?>(menu.AddPage(OptionalString(parameters, "pageId"))));
        server.Register("surface.menu.page.remove", parameters =>
            Task.FromResult<object?>(menu.RemovePage(RequiredString(parameters, "pageId"))));
        server.Register("surface.menu.rule.add", _ =>
            Task.FromResult<object?>(menu.AddRuleForForeground()));
        server.Register("surface.menu.block", _ =>
            Task.FromResult<object?>(menu.BlockForeground()));
        server.Register("surface.menu.move", parameters =>
            Task.FromResult<object?>(menu.MoveCell(
                RequiredString(parameters, "entryId"),
                RequiredString(parameters, "pageId"),
                RequiredInt(parameters, "row"),
                RequiredInt(parameters, "column"))));
    }

    private object State() => menu.Snapshot() with { Pinned = Overlay?.Pinned == true };

    /// <summary>Holds the menu open while one of its own native dialogs is in front.</summary>
    private async Task<object?> Suspended(Func<Task<object?>> work)
    {
        if (Overlay is { } overlay)
        {
            overlay.AutoCloseSuspended = true;
        }

        try
        {
            return await work();
        }
        finally
        {
            if (Overlay is { } done)
            {
                done.AutoCloseSuspended = false;
            }
        }
    }

    private static string RequiredString(JsonElement parameters, string name)
        => parameters.TryGetProperty(name, out var value) && value.GetString() is { Length: > 0 } text
            ? text
            : throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.BadArgument"));

    private static string? OptionalString(JsonElement parameters, string name)
        => parameters.TryGetProperty(name, out var value) ? value.GetString() : null;

    private static int RequiredInt(JsonElement parameters, string name)
        => parameters.TryGetProperty(name, out var value) && value.TryGetInt32(out var number)
            ? number
            : throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.BadArgument"));

    private Task<object?> Activate(JsonElement parameters)
    {
        var entryId = parameters.TryGetProperty("entryId", out var named) ? named.GetString() : null;
        if (entryId is not { Length: > 0 })
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.BadArgument"));
        }

        // The menu goes away before the macro runs, so the window the macro is about gets the
        // foreground back instead of the overlay sitting on top of it.
        Overlay?.Hide();
        return Task.FromResult<object?>(new { started = menu.Activate(entryId) });
    }
}
