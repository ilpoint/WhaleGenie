using System;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Editing;
using WhaleGenie.Localization;

namespace WhaleGenie.Bridge;

// Opening a desktop picker for one action field. The host owns the overlay; the session owns what
// the field means, so the answer is applied by the same code the form edits.
internal sealed partial class EditorSessionBridge
{
    private async Task<object?> ActionPick(JsonElement parameters)
    {
        if (_pickers is null)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Editor.PickUnavailable"));
        }

        var name = RequiredString(parameters, "name");
        var mode = RequiredString(parameters, "mode");
        var index = OptionalInt(parameters, "index") ?? -1;
        try
        {
            var session = Required().ActionForm;
            var request = session.PreparePick(name, index, mode)
                ?? throw new BridgeFailureException(BridgeErrors.BadArgument,
                    Strings.Format("Editor.PickNeedsWindow", name));
            var result = await _pickers.PickAsync(request);
            return session.ApplyPick(request, result);
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private async Task<object?> ActionWindowPick(JsonElement parameters)
    {
        if (_pickers is null)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Editor.PickUnavailable"));
        }

        var request = new EditorPickRequest(EditorPickKind.Window, string.Empty, -1, "window",
            0, 0, 0, 0, 0, string.Empty, string.Empty, string.Empty, string.Empty,
            string.Empty, string.Empty, string.Empty, false, false, []);
        var result = await _pickers.PickAsync(request);
        return new
        {
            chosen = result.Chosen,
            rule = result.WindowRuleJson,
            title = result.WindowTitle,
        };
    }
}
