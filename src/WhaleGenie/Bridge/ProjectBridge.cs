using System;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;
using WhaleGenie.Views;

namespace WhaleGenie.Bridge;

/// <summary>
/// The macro list's side of the bridge: the whole project state in one envelope, the two commands
/// the list itself can make — arming or disarming a macro, and the system switch — and the project
/// file operations with their pickers. The page filters, searches and groups locally; the program
/// remains the only one who knows what a macro is and which view it belongs to.
/// </summary>
/// <remarks>
/// The trigger host owns the service and the plan; this handler only writes the same system switch
/// the window and the hotkey write, and tells the page when the document moved. Every change — an
/// edit here, a draft applied by MCP, a package opened — answers as one <c>project.changed</c>
/// event, so the page never keeps a second copy of the truth.
/// </remarks>
public sealed class ProjectBridge : IBridgeHandler, IDisposable
{
    private readonly ProjectWorkspace _workspace;

    private readonly TriggerRuntime _runtime;

    /// <summary>The shell window, whose storage provider opens the file dialogs.</summary>
    private readonly Func<Window?> _window;

    /// <summary>
    /// The system switch the page draws. The trigger host is the one that follows it; this handler
    /// only writes the same object the window and the hotkey write.
    /// </summary>
    private readonly SystemState _system;

    /// <summary>The page's questions: closing everything is the one the list asks before it wipes.</summary>
    private readonly DialogBridge _dialogs;

    /// <summary>Where a macro switch's sound goes; a check hands in a recorder.</summary>
    private readonly Action<UiSound> _playSound;

    private BridgeServer? _server;

    internal ProjectBridge(ProjectWorkspace workspace, TriggerRuntime runtime, SystemState system,
        Func<Window?> window, DialogBridge dialogs, Action<UiSound>? playSound = null)
    {
        _workspace = workspace;
        _runtime = runtime;
        _system = system;
        _window = window;
        _dialogs = dialogs;
        _playSound = playSound ?? AppSound.Play;
    }

    public void Register(BridgeServer server)
    {
        _server = server;
        server.Register("project.state", _ =>
        {
            // The page asked for the current state; a buffered change from before the page was
            // ready is older than this answer.
            server.DropPending("project.changed");
            return Task.FromResult<object?>(State());
        });
        server.Register("project.toggle", Toggle);
        server.Register("project.delete", Delete);
        server.Register("project.export", Export);
        server.Register("project.system", SystemSwitch);
        server.Register("project.open", Open);
        server.Register("project.import", Import);
        server.Register("project.save", Save);
        server.Register("project.saveAs", SaveAs);
        server.Register("project.reload", Reload);
        server.Register("project.closeAll", CloseAll);
        server.Register("dialog.pickOpenPackage", async _ => await PickOpenPackage());
        server.Register("dialog.pickSavePackage", PickSavePackageHandler);

        // The trigger host owns the plan and the switch; this handler only tells the page when
        // something it draws has moved.
        _system.Changed += OnSystemChanged;
        _runtime.Triggers.StatusChanged += OnTriggerStatusChanged;
        _workspace.Changed += OnProjectChanged;
        _workspace.Macros.CollectionChanged += OnMacrosChanged;
        _workspace.Triggers.Changed += OnTriggersChanged;
    }

    public void Dispose()
    {
        _system.Changed -= OnSystemChanged;
        _runtime.Triggers.StatusChanged -= OnTriggerStatusChanged;
        _workspace.Changed -= OnProjectChanged;
        _workspace.Macros.CollectionChanged -= OnMacrosChanged;
        _workspace.Triggers.Changed -= OnTriggersChanged;
    }

    private object State() => new
    {
        path = _workspace.CurrentPath,
        name = _workspace.CurrentPath is { } path ? System.IO.Path.GetFileNameWithoutExtension(path) : null,
        untitled = _workspace.CurrentPath is null,
        dirty = _workspace.IsDirty,
        revision = _workspace.Revision,
        systemEnabled = _system.IsEnabled,
        views = Enum.GetNames<MacroView>(),
        groups = _workspace.Macros
            .Select(macro => macro.Group.Trim())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCulture)
            .ToArray(),
        macros = _workspace.Macros.Select(macro => new
        {
            name = macro.Name,
            group = macro.Group,
            description = macro.Description,
            enabled = macro.IsEnabled,
            running = macro.IsRunning,
            triggerSummary = macro.CardTriggerSummary,
            triggerActions = macro.CardTriggerActions,
            triggerPreview = macro.CardTriggerPreview,
            views = Enum.GetNames<MacroView>()
                .Where(view => _workspace.Triggers.InView(macro.Name, Enum.Parse<MacroView>(view)))
                .ToArray(),
        }).ToArray(),
    };

    private Task<object?> Toggle(JsonElement parameters)
    {
        var wanted = parameters.TryGetProperty("name", out var named) ? named.GetString() : null;
        var macro = _workspace.Macros.FirstOrDefault(candidate =>
            string.Equals(candidate.Name.Trim(), wanted, StringComparison.OrdinalIgnoreCase));
        if (macro is null)
        {
            throw new BridgeFailureException(BridgeErrors.NoSuchMacro,
                Strings.Format("Bridge.NoSuchMacro", wanted ?? string.Empty));
        }

        _workspace.ToggleMacro(macro);

        // Arming a macro again gives a "trigger once" macro a fresh chance to run; disarming one
        // stops the run it may be in. The main window's toggle does both, so this one does too.
        if (macro.IsEnabled)
        {
            _runtime.Triggers.Rearm(macro.Name);
        }
        else
        {
            _runtime.Triggers.Stop(macro.Name);
        }

        // The same feedback the native card gives. The page does not synthesize its own sound:
        // one owner keeps the setting and the double-tone out of the loop.
        _playSound(macro.IsEnabled ? UiSound.MacroOn : UiSound.MacroOff);

        return Task.FromResult<object?>(null);
    }

    /// <summary>
    /// Removes a macro from the list. The page asks first — the answer comes back through the
    /// confirm bridge — and the run the trigger may be in stops with it.
    /// </summary>
    private async Task<object?> Delete(JsonElement parameters)
    {
        var macro = Find(parameters);
        var accepted = await _dialogs.ConfirmAsync(
            Strings.Get("Editor.DeleteTitle"),
            Strings.Format("Editor.DeleteMessage", macro.Name),
            Strings.Get("Editor.Delete"),
            Strings.Get("Common.Cancel"));
        if (!accepted)
        {
            return null;
        }

        _runtime.Triggers.Stop(macro.Name);
        _workspace.RemoveMacro(macro);
        await PushState();
        return null;
    }

    /// <summary>
    /// Writes one macro and what it needs into a package of its own. The choosing and the asking
    /// happen in the two native windows the main window uses — the picker wants a screen's worth of
    /// list, which is not the shell's job to imitate.
    /// </summary>
    private async Task<object?> Export(JsonElement parameters)
    {
        var macro = Find(parameters);
        if (_window() is not { } window)
        {
            return null;
        }

        var nodes = await MacroChooseWindow.ShowAsync(window, MacroChooseKind.Export,
            _workspace.Macros, _workspace.Triggers, _workspace.Variables.Globals,
            preselected: [macro]);
        if (nodes is null || nodes.Count == 0)
        {
            return null;
        }

        var selection = nodes.Select(node => node.Macro).ToList();
        var path = await PackagePicker.AskWhereAsync(window, Strings.Get("Main.ExportMacroTitle"),
            selection[0].Name);
        if (path is null)
        {
            return null;
        }

        try
        {
            // An export is written somewhere else, while the pictures stay beside the project: the
            // folder comes along so the package carries them rather than pointing at this machine.
            MacroPackage.Save(path, MacroBundle.Copies(selection),
                MacroBundle.Take(_workspace.Triggers, selection),
                MacroBundle.UsedGlobals(selection, _workspace.Variables.Globals), AppVersion.Text,
                ImageAssets.FolderFor(_workspace.CurrentPath));
            return null;
        }
        catch (Exception error)
        {
            throw new BridgeFailureException(BridgeErrors.PackageSaveFailed,
                Strings.Format("Bridge.PackageSaveFailed", Path.GetFileName(path), error.Message));
        }
    }

    private MacroItem Find(JsonElement parameters)
    {
        var wanted = parameters.TryGetProperty("name", out var named) ? named.GetString() : null;
        var macro = _workspace.Macros.FirstOrDefault(candidate =>
            string.Equals(candidate.Name.Trim(), wanted, StringComparison.OrdinalIgnoreCase));
        if (macro is null)
        {
            throw new BridgeFailureException(BridgeErrors.NoSuchMacro,
                Strings.Format("Bridge.NoSuchMacro", wanted ?? string.Empty));
        }

        return macro;
    }

    /// <summary>
    /// Imports from a package, through the same two native windows the main window uses: the
    /// chooser that says what is inside, and the conflicts window that asks what a variable both
    /// sides declare should become. Everything that arrives lands in the project, and the page
    /// hears about it with the usual change event; the report comes back as a question with one
    /// button, because "what did it do" is a report and not a choice.
    /// </summary>
    private async Task<object?> Import(JsonElement parameters)
    {
        if (_window() is not { } window)
        {
            return null;
        }

        var path = await PickAsync(new FilePickerOpenOptions
        {
            Title = Strings.Get("Main.ImportTitle"),
            AllowMultiple = false,
            FileTypeFilter = [PackageFileType()],
        });
        if (path is null)
        {
            return null;
        }

        PackageContents contents;
        try
        {
            // Pictures land in this project's folder: the file being imported is not where the
            // macros will be working from. A name already used there is compared by content, so an
            // arriving picture never replaces one another macro looks for.
            contents = MacroPackage.Load(path, ImageAssets.FolderFor(_workspace.CurrentPath));
        }
        catch (Exception error)
        {
            throw new BridgeFailureException(BridgeErrors.PackageOpenFailed,
                Strings.Format("Bridge.PackageOpenFailed", Path.GetFileName(path), error.Message));
        }

        var nodes = await MacroChooseWindow.ShowAsync(window, MacroChooseKind.Import,
            contents.Macros, contents.Triggers, _workspace.Variables.Globals, contents.Globals,
            fileName: Path.GetFileName(path));
        if (nodes is null || nodes.Count == 0)
        {
            return null;
        }

        var chosen = nodes.Select(node => node.Macro).ToList();

        try
        {
            if (nodes.All(node => node.IsSub))
            {
                // Submacros ticked on their own belong in the public library, because that is
                // where something a macro calls lives.
                var added = MacroImport.IntoLibrary(chosen, AppPaths.PublicMacroLibrary);
                await _dialogs.ConfirmAsync(Strings.Get("Import.ReportTitle"),
                    Strings.Format("Import.LibraryAdded",
                        string.Join("、", added.Select(entry => entry.Macro.Name))),
                    Strings.Get("Common.Ok"), null);
                return null;
            }

            // What to do about a variable both sides declare is a question, not a default: the wrong
            // answer is a macro that reads somebody else's number without saying so.
            var decisions = MacroImport.Decisions(chosen, contents.Globals,
                _workspace.Variables.Globals);
            if (!await GlobalConflictWindow.ShowAsync(window, decisions, chosen))
            {
                return null;
            }

            var report = MacroImport.IntoProject(_workspace, contents, chosen, decisions);
            await _dialogs.ConfirmAsync(Strings.Get("Import.ReportTitle"),
                MainWindow.ReportText(report), Strings.Get("Common.Ok"), null);
            return null;
        }
        catch (Exception error)
        {
            throw new BridgeFailureException(BridgeErrors.PackageOpenFailed,
                Strings.Format("Bridge.PackageOpenFailed", Path.GetFileName(path), error.Message));
        }
    }

    private async Task<object?> SystemSwitch(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("enabled", out var given)
            || given.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.SystemNeedsFlag"));
        }

        // The switch is the same object the window and the hotkey write; the trigger host answers
        // to it with the service, the sound and the plan republish.
        _system.Set(given.GetBoolean());
        return null;
    }

    /// <summary>
    /// Opens a package. Without a path the shell's open dialog comes first; a package that cannot
    /// be read is named to the page with the same words the main window would have used, and the
    /// project stays as it was.
    /// </summary>
    private async Task<object?> Open(JsonElement parameters)
    {
        var path = parameters.TryGetProperty("path", out var given) && given.ValueKind == JsonValueKind.String
            ? given.GetString()
            : null;
        var picked = path is { Length: > 0 } ? path : await PickOpenPackage();
        if (picked is not { Length: > 0 } openPath)
        {
            return null;
        }

        await OpenPackage(openPath);
        return null;
    }

    /// <summary>
    /// Writes the project out. With no file yet — or with a path handed in from "save as" — the
    /// save dialog asks where; a save that was called off changes nothing.
    /// </summary>
    private async Task<object?> Save(JsonElement parameters)
    {
        var path = _workspace.CurrentPath;
        if (parameters.TryGetProperty("path", out var given) && given.ValueKind == JsonValueKind.String
            && given.GetString() is { Length: > 0 } explicitPath)
        {
            path = explicitPath;
        }

        if (path is not { Length: > 0 })
        {
            return await SaveAs(parameters);
        }

        try
        {
            _workspace.SavePackage(path);
            LocalSettings.StoreLastProject(path);
            await PushState();
            return null;
        }
        catch (Exception error)
        {
            throw new BridgeFailureException(BridgeErrors.PackageSaveFailed,
                Strings.Format("Bridge.PackageSaveFailed", Path.GetFileName(path), error.Message));
        }
    }

    /// <summary>Asks where the project goes, then writes it there.</summary>
    private async Task<object?> SaveAs(JsonElement parameters)
    {
        var suggested = _workspace.CurrentPath is { Length: > 0 } current
            ? Path.GetFileName(current)
            : $"macros{MacroPackage.Extension}";
        if (parameters.TryGetProperty("suggestedName", out var named)
            && named.GetString() is { Length: > 0 } suggestedGiven)
        {
            suggested = suggestedGiven;
        }

        var path = await PickSavePackage(suggested);
        if (path is not { Length: > 0 })
        {
            return null;
        }

        return await Save(JsonSerializer.SerializeToElement(new { path }));
    }

    /// <summary>
    /// Reads the current package back in. This throws away what has not been saved, and that is
    /// what "reload" means — the main window asks nothing before doing exactly the same.
    /// </summary>
    private async Task<object?> Reload(JsonElement parameters)
    {
        if (_workspace.CurrentPath is not { Length: > 0 } path)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.ReloadNeedsFile"));
        }

        await OpenPackage(path);
        return null;
    }

    /// <summary>
    /// Wipes the list, the way "close all" does on the main window: the page asks first, because
    /// an empty list every morning is a step nobody asked for. A project that holds nothing has
    /// nothing to wipe.
    /// </summary>
    private async Task<object?> CloseAll(JsonElement parameters)
    {
        if (_workspace.Macros.Count == 0)
        {
            return null;
        }

        var accepted = await _dialogs.ConfirmAsync(
            Strings.Get("Main.CloseAllTitle"),
            Strings.Get("Main.CloseAllMessage"),
            Strings.Get("Main.CloseAll"),
            null);
        if (!accepted)
        {
            return null;
        }

        _runtime.Triggers.StopAll();
        _workspace.ClearMacros();
        _workspace.CurrentPath = null;
        await PushState();
        return null;
    }

    private async Task OpenPackage(string path)
    {
        try
        {
            _workspace.OpenPackage(path);
            LocalSettings.StoreLastProject(path);
            await PushState();
        }
        catch (Exception error)
        {
            throw new BridgeFailureException(BridgeErrors.PackageOpenFailed,
                Strings.Format("Bridge.PackageOpenFailed", Path.GetFileName(path), error.Message));
        }
    }

    /// <summary>Opens the shell's open dialog for a package; the answer is a path or "called off".</summary>
    private async Task<object?> PickSavePackageHandler(JsonElement parameters)
    {
        var suggested = parameters.TryGetProperty("suggestedName", out var named)
            && named.GetString() is { Length: > 0 } given
            ? given
            : $"macros{MacroPackage.Extension}";
        return await PickSavePackage(suggested);
    }

    private Task<string?> PickOpenPackage()
        => PickAsync(new FilePickerOpenOptions
        {
            Title = Strings.Get("Main.OpenProjectTitle"),
            AllowMultiple = false,
            FileTypeFilter = [PackageFileType()],
        });

    private Task<string?> PickSavePackage(string suggestedName)
        => PickSaveAsync(new FilePickerSaveOptions
        {
            Title = Strings.Get("Main.SaveProjectTitle"),
            SuggestedFileName = suggestedName,
            DefaultExtension = MacroPackage.Extension.TrimStart('.'),
            FileTypeChoices = [PackageFileType()],
            ShowOverwritePrompt = true,
        });

    private static FilePickerFileType PackageFileType()
        => new(Strings.Get("Package.Filter")) { Patterns = [$"*{MacroPackage.Extension}"] };

    private async Task<string?> PickAsync(FilePickerOpenOptions options)
    {
        if (_window() is not { } window)
        {
            return null;
        }

        var files = await window.StorageProvider.OpenFilePickerAsync(options);
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private async Task<string?> PickSaveAsync(FilePickerSaveOptions options)
    {
        if (_window() is not { } window)
        {
            return null;
        }

        var file = await window.StorageProvider.SaveFilePickerAsync(options);
        return file?.TryGetLocalPath();
    }

    /// <summary>
    /// A trigger started or stopped a macro, so the running dots move. The service says so from
    /// its own thread; the macro states are written here, and the page hears about them with the
    /// same event every other change rides on.
    /// </summary>
    private void OnTriggerStatusChanged()
        => _ = _server?.PushAsync("project.changed", State());

    private void OnSystemChanged()
        => _ = _server?.PushAsync("project.changed", State());

    private void OnProjectChanged(object? sender, EventArgs args)
        => _ = _server?.PushAsync("project.changed", State());

    private void OnMacrosChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        // Opening a package replaces the collection without raising a project change of its own.
        _ = _server?.PushAsync("project.changed", State());
    }

    private void OnTriggersChanged(object? sender, EventArgs args)
        => _ = _server?.PushAsync("project.changed", State());

    private Task PushState() => _server!.PushAsync("project.changed", State());
}
