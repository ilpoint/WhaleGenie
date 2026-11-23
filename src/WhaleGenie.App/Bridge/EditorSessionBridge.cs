using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Contracts;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Recording;
using WhaleGenie.Editing;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Bridge;

/// <summary>
/// The C0 editor session over the bridge: open a macro, read the snapshot, change fields, add a
/// step, save, close. The native editor is still the one a person uses; this is the path the page
/// will take in C1, and the gate that proves it can be built without moving the model out of C#.
/// </summary>
internal sealed partial class EditorSessionBridge(ProjectWorkspace workspace,
    IDeviceLayer? devices = null, IEditorSettings? settings = null,
    IEditorPickerHost? pickers = null, IEditorWindowAside? aside = null,
    IEditorBrowserTestHost? browserTests = null, IEditorElementTestHost? elementTests = null)
    : IBridgeHandler
{
    private readonly HeadlessEditorWorkspace _documents = new(workspace, devices,
        settings ?? DefaultEditorSettings.Instance);
    private readonly IDeviceLayer? _devices = devices;
    private readonly IEditorSettings _settings = settings ?? DefaultEditorSettings.Instance;
    private readonly IEditorPickerHost? _pickers = pickers;
    private readonly IEditorWindowAside? _aside = aside;
    private readonly IEditorBrowserTestHost? _browserTests = browserTests;
    private readonly IEditorElementTestHost? _elementTests = elementTests;
    private RecordingSession? _recording;
    private volatile bool _stopRecordingRequested;

    public void Register(BridgeServer server)
    {
        server.Register("editor.session", SessionAction);
        server.Register("editor.sessionTabs", _ => Task.FromResult<object?>(Tabs()));
        server.Register("editor.declaration", DeclarationAction);
        server.Register("editor.sessionState", _ => Task.FromResult<object?>(Snapshot()));
        server.Register("editor.step", StepAction);
        server.Register("editor.condition", ConditionAction);
        server.Register("editor.steps", StepsAction);

        server.Register("editor.trigger", TriggerAction);
        server.Register("editor.variable", VariableAction);
        server.Register("editor.record", RecordAction);
        server.Register("editor.sessionActions", _ => Task.FromResult<object?>(Actions()));
        server.Register("editor.action", ActionCommand);
        server.Register("editor.actionOpen", ActionOpen);
        server.Register("editor.actionConfirm", _ => Task.FromResult<object?>(Required().ConfirmActionDraft()));
        server.Register("editor.actionCancel", _ => Task.FromResult<object?>(Required().CancelActionDraft()));
        server.Register("editor.actionWindowPreview", ActionWindowPreview);
        server.Register("editor.actionPick", ActionPick);
        server.Register("editor.actionLook", ActionLook);
        server.Register("editor.actionTestBrowser", ActionTestBrowser);
        server.Register("editor.actionTestElement", ActionTestElement);
        server.Register("editor.actionTestNarrow", ActionTestNarrow);
        server.Register("editor.actionWindowPick", ActionWindowPick);
        server.Register("editor.actionRun", ActionRun);
        server.Register("editor.actionKeyPad", ActionKeyPad);
        server.Register("editor.hotstringKeyPad", HotstringKeyPad);
        server.Register("editor.value", ValueAction);
    }



    private object? Snapshot()
    {
        var active = _documents.Active;
        if (active?.Snapshot() is not { } snapshot)
        {
            return null;
        }

        return new
        {
            name = snapshot.Name,
            description = snapshot.Description,
            group = snapshot.Group,
            subMacros = snapshot.SubMacros,
            callableParameters = snapshot.CallableParameters,
            callableResults = snapshot.CallableResults,
            followInputLimit = snapshot.FollowInputLimit,
            inputLimit = snapshot.InputLimit,
            toggleKey = snapshot.ToggleKey,
            dirty = snapshot.Dirty,
            stepCount = snapshot.StepCount,
            steps = snapshot.Steps,
            triggers = snapshot.Triggers,
            variables = snapshot.Variables,
            globals = snapshot.Globals,
            valueChoices = snapshot.ValueChoices,
            valueFunctions = snapshot.ValueFunctions,
            conditionCompares = snapshot.ConditionCompares,
            variableTypes = snapshot.VariableTypes,
            triggerKinds = snapshot.TriggerKinds,
            canUndo = snapshot.CanUndo,
            canRedo = snapshot.CanRedo,
            canSave = snapshot.CanSave,
            nameProblem = snapshot.NameProblem,
            problemSummary = snapshot.ProblemSummary,
            declarationProblemSummary = snapshot.DeclarationProblemSummary,
            callImpactSummary = snapshot.CallImpactSummary,
            notice = snapshot.Notice,
            triggerProblemSummary = snapshot.TriggerProblemSummary,
            toggleKeyProblem = snapshot.ToggleKeyProblem,
            recording = snapshot.Recording,
            isSubMacro = snapshot.IsSubMacro,
            tabs = _documents.Tabs,
            activeKey = _documents.ActiveKey,
            actionForm = active.ActionFormState(),
            revision = snapshot.Revision,
        };
    }

    private static readonly JsonSerializerOptions CommandOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static string? OptionalString(JsonElement parameters, string name)
        => parameters.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? OptionalBool(JsonElement parameters, string name)
        => parameters.TryGetProperty(name, out var value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? value.GetBoolean()
                : null;

    private static int? OptionalInt(JsonElement parameters, string name)
        => parameters.TryGetProperty(name, out var value) && value.TryGetInt32(out var number)
            ? number
            : null;

    private static double? OptionalDouble(JsonElement parameters, string name)
        => parameters.TryGetProperty(name, out var value) && value.TryGetDouble(out var number)
            ? number
            : null;

    private static int[]? OptionalPath(JsonElement parameters, string name)
        => parameters.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? [.. value.EnumerateArray().Select(item => item.GetInt32())]
            : null;



    private static EditorCatalogue Actions()
        => new(
            ActionCatalog.RunnableActions.Count,
            HeadlessEditorSession.Actions(),
            HeadlessEditorSession.ActionCategories(),
            HeadlessEditorSession.PublicMacros());

    private HeadlessEditorSession Required()
        => _documents.Active ?? throw new BridgeFailureException(BridgeErrors.BadArgument,
            Strings.Get("Bridge.EditorNoSession"));
}
