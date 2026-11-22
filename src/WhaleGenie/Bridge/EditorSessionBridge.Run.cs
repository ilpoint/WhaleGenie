using System;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Editing;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Views;

namespace WhaleGenie.Bridge;

// Trying the draft step once, with every window of this program out of the way.
internal sealed partial class EditorSessionBridge
{
    private async Task<object?> ActionRun(JsonElement parameters)
    {
        var session = Required();
        var form = session.ActionForm;
        if (!form.Form.CanSave)
        {
            return new { needsConfirm = false, refused = form.Form.ValidationMessage };
        }

        if (form.CannotBeUndone && OptionalBool(parameters, "confirmed") != true)
        {
            return new
            {
                needsConfirm = true,
                title = Strings.Get("Run.CannotBeUndoneTitle"),
                question = form.IrreversibleQuestion,
            };
        }

        if (_devices is null)
        {
            throw new BridgeFailureException(BridgeErrors.Internal,
                Strings.Get("Editor.PickUnavailable"));
        }

        var library = new ProjectMacroLibrary(CallableMacros.Entries(workspace.Macros));
        async Task<EditorTrialResult> Run() => await session.ActionForm.RunTrialAsync(
            _devices, library, workspace.Variables);

        EditorTrialResult result;
        if (_owner?.Invoke() is { } owner)
        {
            using var aside = AppWindows.StepAside(owner);
            await AppWindows.SettleAsync(_devices.Screen);
            result = await Task.Run(Run);
        }
        else
        {
            result = await Task.Run(Run);
        }

        return new { needsConfirm = false, result };
    }
}
