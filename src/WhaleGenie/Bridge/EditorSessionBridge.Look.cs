using System;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Editing;
using WhaleGenie.Localization;
using WhaleGenie.Views;

namespace WhaleGenie.Bridge;

// Looking at the screen once, then picking an offset or a pattern of colours on that picture.
internal sealed partial class EditorSessionBridge
{
    private async Task<object?> ActionLook(JsonElement parameters)
    {
        if (_pickers is not IEditorLookHost lookHost || _devices is null)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Editor.PickUnavailable"));
        }

        var name = RequiredString(parameters, "name");
        var mode = OptionalString(parameters, "mode") ?? "offset";
        var session = Required().ActionForm;
        try
        {
            EditorLookRequest request;
            if (_owner?.Invoke() is { } owner)
            {
                using (AppWindows.StepAside(owner))
                {
                    await AppWindows.SettleAsync(_devices.Screen);
                    request = await Task.Run(() => session.PrepareLook(_devices, name, mode));
                }
            }
            else
            {
                request = await Task.Run(() => session.PrepareLook(_devices, name, mode));
            }

            var result = await lookHost.PickLookAsync(request);
            return session.ApplyLook(request, result);
        }
        catch (InvalidOperationException error)
        {
            return session.Say(error.Message, true);
        }
    }
}
