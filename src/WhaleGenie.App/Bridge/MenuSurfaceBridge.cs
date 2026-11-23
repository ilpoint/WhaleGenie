using System.Threading.Tasks;
using WhaleGenie.Contracts;
using WhaleGenie.Localization;
using WhaleGenie.Runtime;

namespace WhaleGenie.Bridge;

/// <summary>
/// The menu's side of the bridge: the pages the project has and the cells they put macros in. The
/// overlay asks once when it opens; paging and search are the page's own state, so nothing here
/// pushes.
/// </summary>
internal sealed class MenuSurfaceBridge(MenuSurface menu) : IBridgeHandler
{
    /// <summary>The projection itself, so the shell can hand it the interface behind the page.</summary>
    internal MenuSurface Menu => menu;

    /// <summary>The window the page speaks through, once the shell has built it.</summary>
    internal IMenuOverlay? Overlay { get; set; }

    public void Register(BridgeServer server)
    {
        server.Register("surface.menu.state", _ => Task.FromResult<object?>(menu.Snapshot()));
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
                && given.ValueKind == System.Text.Json.JsonValueKind.True;
            Overlay?.SetSearchFocused(focused);
            return Task.FromResult<object?>(null);
        });
        server.Register("surface.menu.pin", parameters =>
        {
            var pinned = parameters.TryGetProperty("pinned", out var given)
                && given.ValueKind == System.Text.Json.JsonValueKind.True;
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
        server.Register("surface.menu.replace", async parameters =>
            await menu.ReplaceAsync(RequiredString(parameters, "entryId")));
        server.Register("surface.menu.icon", async parameters =>
            await menu.ChooseIconAsync(RequiredString(parameters, "entryId")));
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

    private static string RequiredString(System.Text.Json.JsonElement parameters, string name)
        => parameters.TryGetProperty(name, out var value) && value.GetString() is { Length: > 0 } text
            ? text
            : throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.BadArgument"));

    private static string? OptionalString(System.Text.Json.JsonElement parameters, string name)
        => parameters.TryGetProperty(name, out var value) ? value.GetString() : null;

    private static int RequiredInt(System.Text.Json.JsonElement parameters, string name)
        => parameters.TryGetProperty(name, out var value) && value.TryGetInt32(out var number)
            ? number
            : throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.BadArgument"));

    private Task<object?> Activate(System.Text.Json.JsonElement parameters)
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
