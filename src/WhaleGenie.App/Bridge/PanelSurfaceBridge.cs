using System.Threading.Tasks;
using WhaleGenie.Contracts;
using WhaleGenie.Runtime;

namespace WhaleGenie.Bridge;

/// <summary>One interface panel's frame; the page asks again when its group or members change.</summary>
internal sealed class PanelSurfaceBridge(PanelSurface panel) : IBridgeHandler
{
    public void Register(BridgeServer server)
    {
        server.Register("surface.panel.run", parameters =>
        {
            var name = parameters.TryGetProperty("name", out var given) ? given.GetString() : null;
            return Task.FromResult<object?>(new { started = panel.Run(name ?? string.Empty) });
        });
        server.Register("surface.panel.view", parameters =>
        {
            var key = parameters.TryGetProperty("groupKey", out var group) ? group.GetString() ?? "" : "";
            return Task.FromResult<object?>(panel.View(
                key,
                OptionalBool(parameters, "collapsed"),
                OptionalBool(parameters, "horizontal")));
        });
        server.Register("surface.panel.state", parameters =>
        {
            var key = parameters.TryGetProperty("groupKey", out var group) ? group.GetString() ?? "" : "";
            var horizontal = parameters.TryGetProperty("horizontal", out var side)
                && side.ValueKind == System.Text.Json.JsonValueKind.True;
            var collapsed = parameters.TryGetProperty("collapsed", out var fold)
                && fold.ValueKind == System.Text.Json.JsonValueKind.True;
            return Task.FromResult<object?>(panel.Snapshot(key, horizontal, collapsed));
        });
    }

    private static bool? OptionalBool(System.Text.Json.JsonElement parameters, string name)
        => parameters.TryGetProperty(name, out var value)
            ? value.ValueKind == System.Text.Json.JsonValueKind.True
            : null;
}
