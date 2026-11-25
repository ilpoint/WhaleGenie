using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Contracts;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Recording;
using WhaleGenie.Editing;
using WhaleGenie.Editor;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Projects;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Bridge;

/// <summary>
/// The C0 editor session over the bridge: open a macro, read the snapshot, change fields, add a
/// step, save, close. The native editor is still the one a person uses; this is the path the page
/// will take in C1, and the gate that proves it can be built without moving the model out of C#.
/// </summary>
internal sealed partial class EditorSessionBridge(EditorDocumentService documents,
    ProjectWorkspace workspace,
    IDeviceLayer? devices = null, IEditorSettings? settings = null,
    IEditorPickerHost? pickers = null, IEditorWindowAside? aside = null,
    IEditorBrowserTestHost? browserTests = null, IEditorElementTestHost? elementTests = null,
    IPublicLibraryStore? publicLibrary = null, IPackageStore? packages = null,
    AppSettings? appSettings = null)
    : IBridgeHandler
{
    private readonly EditorDocumentService _documents = documents;
    private readonly AppSettings? _appSettings = appSettings;
    private readonly IPublicLibraryStore _publicLibrary =
        publicLibrary ?? EmptyPublicLibraryStore.Instance;
    private readonly IPackageStore _packages = packages ?? new FilePackageStore();
    private readonly IDeviceLayer? _devices = devices;
    private readonly IEditorSettings _settings = settings ?? DefaultEditorSettings.Instance;
    private readonly IEditorPickerHost? _pickers = pickers;
    private readonly IEditorWindowAside? _aside = aside;
    private readonly IEditorBrowserTestHost? _browserTests = browserTests;
    private readonly IEditorElementTestHost? _elementTests = elementTests;
    private RecordingSession? _recording;
    private volatile bool _stopRecordingRequested;

    /// <summary>
    /// The order every editor snapshot reaches the page in. It belongs to the bridge rather than to
    /// one document: the page keeps one strip with several documents, and a new document must not
    /// carry a number lower than the one the page is already showing.
    /// </summary>
    private int _snapshotRevision;

    private BridgeServer? _server;

    public void Register(BridgeServer server)
    {
        _server = server;
        _documents.Changed += OnDocumentsChanged;
        workspace.LibraryChanged += (_, _) => OnLibraryChanged();
        server.Register("editor.session", SessionAction);
        server.Register("editor.save", SaveAction);
        server.Register("editor.saveAll", SaveAllAction);
        server.Register("editor.discardAll", _ => Task.FromResult<object?>(DiscardAllAction()));
        server.Register("editor.unsavedDocuments", _ => Task.FromResult<object?>(
            new EditorUnsavedDocumentsSnapshot(_documents.UnsavedDocuments)));
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
        server.Register("editor.actionConfirm", _ => Task.FromResult<object?>(ActionConfirm()));
        server.Register("editor.actionCancel", _ => Task.FromResult<object?>(ActionCancel()));
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
        var snapshot = _documents.Snapshot();
        return snapshot is null ? null : Envelope(snapshot);
    }

    /// <summary>
    /// Committing or cancelling an action draft changes the document's steps and its dirty flag.
    /// The answer has to be the same bridge snapshot every other edit returns — tabs, active id
    /// and the action form included — or the page's revision gate can drop it and the new step
    /// never appears in the list.
    /// </summary>
    private object? ActionConfirm()
    {
        Required().ConfirmActionDraft();
        return Snapshot();
    }

    private object? ActionCancel()
    {
        Required().CancelActionDraft();
        return Snapshot();
    }

    /// <summary>
    /// One snapshot in the shape the page draws: the active document, the strip beside it and the
    /// action being written. The bridge owns the wire revision because the page orders the whole
    /// strip, while a document's own counter only orders that session's answers.
    /// </summary>
    private EditorSnapshot Envelope(EditorSnapshot snapshot)
        => snapshot with
        {
            ActionForm = _documents.Active?.ActionFormState(),
            Revision = Interlocked.Increment(ref _snapshotRevision),
        };

    /// <summary>The answer to an explicit save, with one result per document the command touched.</summary>
    private EditorSaveResponse SaveResponse(IReadOnlyList<EditorDocumentSaveResult> results)
        => new(_documents.Snapshot() is { } snapshot ? Envelope(snapshot) : null, results);

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



    private EditorCatalogue Actions()
        => new(
            ActionCatalog.RunnableActions.Count,
            HeadlessEditorSession.Actions(),
            HeadlessEditorSession.ActionCategories(),
            PublicMacros(),
            HeadlessEditorSession.ValueFunctions,
            HeadlessEditorSession.VariableTypes,
            HeadlessEditorSession.ConditionCompares,
            HeadlessEditorSession.TriggerKindOptions,
            HeadlessEditorSession.SystemValueChoices);

    private IReadOnlyList<EditorPublicMacroSnapshot> PublicMacros()
    {
        var snapshots = new List<EditorPublicMacroSnapshot>();
        var sources = _appSettings?.LoadPublicMacroEditorSources() ?? "both";
        if (sources != "local")
        {
            snapshots.AddRange(workspace.Library.Select(entry => Snapshot(entry, "project")));
        }

        if (sources == "project")
        {
            return snapshots;
        }

        try
        {
            snapshots.AddRange(_publicLibrary.Load().Select(entry => Snapshot(entry, "local")));
        }
        catch (Exception)
        {
            // A broken machine library must not hide the project's own shelf.
        }

        return snapshots;
    }

    private static EditorPublicMacroSnapshot Snapshot(PublicMacroEntry entry, string source)
        => new(entry.Id, entry.Version, entry.Macro.Name, entry.Macro.Description, source);

    private HeadlessEditorSession Required()
        => _documents.Required();

    /// <summary>
    /// The documents moved outside a page command — a project replacement closed them, or the
    /// exit question discarded them. The strip in the page has to hear it even though no command
    /// it sent produced the snapshot.
    /// </summary>
    private void OnDocumentsChanged()
        => _ = _server?.PushAsync("editor.changed", Snapshot());

    private void OnLibraryChanged()
        => _ = _server?.PushAsync("editor.publicMacros", PublicMacros());

    /// <summary>Re-sends the public-macro rows after a setting changes which shelves are listed.</summary>
    internal void PublishPublicMacros()
        => _ = _server?.PushAsync("editor.publicMacros", PublicMacros());
}
