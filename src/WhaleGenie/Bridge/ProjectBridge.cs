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
/// Registering also wires the trigger service the way the main window does on opening: the system
/// switch starts where the settings say it should, and the trigger plan is published for the
/// project as it stands. Every change to the document — an edit here, a draft applied by MCP, a
/// package opened — answers as one <c>project.changed</c> event, so the page never keeps a second
/// copy of the truth.
/// </remarks>
public sealed class ProjectBridge : IBridgeHandler, IDisposable
{
    private readonly ProjectWorkspace _workspace;

    private readonly TriggerRuntime _runtime;

    /// <summary>The shell window, whose storage provider opens the file dialogs.</summary>
    private readonly Func<Window?> _window;

    /// <summary>The page's questions: closing everything is the one the list asks before it wipes.</summary>
    private readonly DialogBridge _dialogs;

    private BridgeServer? _server;

    internal ProjectBridge(ProjectWorkspace workspace, TriggerRuntime runtime, Func<Window?> window,
        DialogBridge dialogs)
    {
        _workspace = workspace;
        _runtime = runtime;
        _window = window;
        _dialogs = dialogs;
    }

    public void Register(BridgeServer server)
    {
        _server = server;
        server.Register("project.state", _ => Task.FromResult<object?>(State()));
        server.Register("project.toggle", Toggle);
        server.Register("project.delete", Delete);
        server.Register("project.export", Export);
        server.Register("project.system", SystemSwitch);
        server.Register("project.open", Open);
        server.Register("project.save", Save);
        server.Register("project.saveAs", SaveAs);
        server.Register("project.reload", Reload);
        server.Register("project.closeAll", CloseAll);
        server.Register("dialog.pickOpenPackage", async _ => await PickOpenPackage());
        server.Register("dialog.pickSavePackage", PickSavePackageHandler);

        // The trigger service starts the way the main window starts it: armed or not by the
        // setting, and given the project as it stands now.
        _runtime.Triggers.IsEnabled = LocalSettings.LoadSystemAtStart();
        PublishPlan();
        _runtime.Triggers.StatusChanged += OnTriggerStatusChanged;
        _workspace.Changed += OnProjectChanged;
        _workspace.Macros.CollectionChanged += OnMacrosChanged;
        _workspace.Triggers.Changed += OnTriggersChanged;
    }

    public void Dispose()
    {
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
        systemEnabled = _runtime.Triggers.IsEnabled,
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
            throw new BridgeFailureException(BridgeErrors.NoSuchMacro, $"There is no macro named \"{wanted}\".");
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
                $"{Path.GetFileName(path)}\n{error.Message}");
        }
    }

    private MacroItem Find(JsonElement parameters)
    {
        var wanted = parameters.TryGetProperty("name", out var named) ? named.GetString() : null;
        var macro = _workspace.Macros.FirstOrDefault(candidate =>
            string.Equals(candidate.Name.Trim(), wanted, StringComparison.OrdinalIgnoreCase));
        if (macro is null)
        {
            throw new BridgeFailureException(BridgeErrors.NoSuchMacro, $"There is no macro named \"{wanted}\".");
        }

        return macro;
    }

    private async Task<object?> SystemSwitch(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("enabled", out var given)
            || given.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, "project.system needs an enabled flag");
        }

        _runtime.Triggers.IsEnabled = given.GetBoolean();
        await PushState();
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
                $"{Path.GetFileName(path)}\n{error.Message}");
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
                "project.reload needs a project that has a file");
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
                $"{Path.GetFileName(path)}\n{error.Message}");
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
    /// Gives the trigger service the project as it stands, the way the main window publishes it on
    /// every change: a run can only see what was published before it started.
    /// </summary>
    private void PublishPlan()
        => _runtime.Publish(_workspace.Triggers, [.. _workspace.Macros], LocalSettings.LoadMenuLauncher());

    /// <summary>
    /// A trigger started or stopped a macro, so the running dots move. The service says so from
    /// its own thread; the macro states are written here, and the page hears about them with the
    /// same event every other change rides on.
    /// </summary>
    private void OnTriggerStatusChanged()
        => _ = _server?.PushAsync("project.changed", State());

    private void OnProjectChanged(object? sender, EventArgs args)
    {
        PublishPlan();
        _ = _server?.PushAsync("project.changed", State());
    }

    private void OnMacrosChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        // Opening a package replaces the collection without raising a project change of its own.
        PublishPlan();
        _ = _server?.PushAsync("project.changed", State());
    }

    private void OnTriggersChanged(object? sender, EventArgs args)
        => _ = _server?.PushAsync("project.changed", State());

    private Task PushState() => _server!.PushAsync("project.changed", State());
}
