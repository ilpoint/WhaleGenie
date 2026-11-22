using System;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;

namespace WhaleGenie.Bridge;

// The read-only "did the pick take the right thing" buttons beside an element field.
internal sealed partial class EditorSessionBridge
{
    private async Task<object?> ActionTestBrowser(JsonElement parameters)
    {
        if (_devices is null)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Editor.PickUnavailable"));
        }

        var session = Required().ActionForm;
        var name = OptionalString(parameters, "name") ?? "pageSelector";
        var target = session.BrowserTarget(name);
        if (target.Selectors.Count == 0)
        {
            return session.Say(Strings.Get("Add.TestElementEmpty"), true);
        }

        try
        {
            var found = await _browserTests!.HighlightAsync(_devices.Browser, target);
            return session.Say(found.Found
                ? Strings.Format("Add.TestPageElementFound", found.Line, found.Matches)
                : Strings.Get("Add.TestPageElementMissing"), !found.Found);
        }
        catch (Exception error)
        {
            return session.Say(error.Message, true);
        }
    }

    private async Task<object?> ActionTestNarrow(JsonElement parameters)
    {
        if (_devices is null)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Editor.PickUnavailable"));
        }

        var session = Required().ActionForm;
        var name = RequiredString(parameters, "name");
        var index = OptionalInt(parameters, "index") ?? 0;
        var target = session.BrowserTarget("pageSelector", index);
        if (target.Selectors.Count == 0)
        {
            return session.Say(Strings.Get("Add.TestElementEmpty"), true);
        }

        try
        {
            var found = await _browserTests!.HighlightAsync(_devices.Browser, target);
            return session.SayNarrow(name, index, found);
        }
        catch (Exception error)
        {
            return session.Say(error.Message, true);
        }
    }

    private async Task<object?> ActionTestElement(JsonElement parameters)
    {
        if (_devices is null)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Editor.PickUnavailable"));
        }

        var session = Required().ActionForm;
        var name = OptionalString(parameters, "name") ?? "selector";
        var selector = session.FieldText(name);
        if (selector.Length == 0)
        {
            return session.Say(Strings.Get("Add.TestElementEmpty"), true);
        }

        var found = _elementTests is not null && await _elementTests.FlashAsync(
            _devices, selector, session.TestWindowRule("window"));
        return session.Say(found
            ? Strings.Get("Add.TestElementFound")
            : Strings.Get("Add.TestElementMissing"), !found);
    }
}
