using System;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Contracts;
using WhaleGenie.Editing;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;

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
            return new EditorTrialReply(false, null, null, form.Form.ValidationMessage, null);
        }

        if (form.CannotBeUndone && OptionalBool(parameters, "confirmed") != true)
        {
            return new EditorTrialReply(true, Strings.Get("Run.CannotBeUndoneTitle"),
                form.IrreversibleQuestion, null, null);
        }

        if (_devices is null)
        {
            throw new BridgeFailureException(BridgeErrors.Internal,
                Strings.Get("Editor.PickUnavailable"));
        }

        var library = new ProjectMacroLibrary(CallableMacros.Entries(workspace.Macros));
        async Task<EditorTrialResult> Run() => await session.ActionForm.RunTrialAsync(
            _devices, library, workspace.Variables);

        using var aside = _aside is null ? null : await _aside.StepAsideAsync(_devices.Screen);
        var result = await Task.Run(Run);

        return new EditorTrialReply(false, null, null, null, result);
    }
}
