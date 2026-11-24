using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Contracts;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Projects;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;

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

    /// <summary>The file pickers; the UI project owns the windows and their filters.</summary>
    private readonly IFileDialogs _files;

    /// <summary>
    /// The system switch the page draws. The trigger host is the one that follows it; this handler
    /// only writes the same object the window and the hotkey write.
    /// </summary>
    private readonly SystemState _system;

    /// <summary>The page's questions: closing everything is the one the list asks before it wipes.</summary>
    private readonly DialogBridge _dialogs;

    /// <summary>Where a macro switch's sound goes; a check hands in a recorder.</summary>
    private readonly Action<UiSound> _playSound;

    /// <summary>Remembers the last package; a check hands in a no-op so it leaves no trace.</summary>
    private readonly Action<string> _rememberProject;

    /// <summary>The settings file behind the last-package memory and the list order.</summary>
    private readonly AppSettings _settings;

    private BridgeServer? _server;

    internal ProjectBridge(ProjectWorkspace workspace, TriggerRuntime runtime, SystemState system,
        IFileDialogs files, DialogBridge dialogs,
        Action<UiSound>? playSound = null, Action<string>? rememberProject = null,
        AppSettings? settings = null)
    {
        _settings = settings ?? AppSettings.Shared;
        _workspace = workspace;
        _runtime = runtime;
        _system = system;
        _files = files;
        _dialogs = dialogs;
        _playSound = playSound ?? (sound => AppSound.Play(_settings, sound));
        _rememberProject = rememberProject ?? _settings.StoreLastProject;
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
        server.Register("project.exportPlan", ExportPlan);
        server.Register("project.exportApply", ExportApply);
        server.Register("project.system", SystemSwitch);
        server.Register("project.open", Open);
        server.Register("project.importPlan", ImportPlan);
        server.Register("project.importApply", ImportApply);
        server.Register("project.save", Save);
        server.Register("project.saveAs", SaveAs);
        server.Register("project.reload", Reload);
        server.Register("project.closeAll", CloseAll);
        server.Register("dialog.pickOpenPackage", async _ => await PickOpenPackage());
        server.Register("dialog.pickSavePackage", PickSavePackageHandler);
        server.Register("dialog.pickImage", PickImage);

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

    private ProjectState State() => new(
        _workspace.CurrentPath,
        _workspace.CurrentPath is { } path ? Path.GetFileNameWithoutExtension(path) : null,
        _workspace.CurrentPath is null,
        _workspace.IsDirty,
        _workspace.Revision,
        _system.IsEnabled,
        _settings.LoadEnabledFirst(),
        Enum.GetNames<MacroView>(),
        [.. _workspace.Macros
            .Select(macro => macro.Group.Trim())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCulture)],
        [.. _workspace.Macros.Select(macro => new MacroEntry(
            macro.Name,
            macro.Group,
            macro.Description,
            macro.IsEnabled,
            macro.IsRunning,
            macro.CardTriggerSummary,
            macro.CardTriggerActions,
            macro.CardTriggerPreview,
            [.. Enum.GetNames<MacroView>()
                .Where(view => _workspace.Triggers.InView(macro.Name, Enum.Parse<MacroView>(view)))]))]);

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

    /// <summary>What an export could hold, before the page asks which macros and where.</summary>
    private Task<object?> ExportPlan(JsonElement _)
        => Task.FromResult<object?>(PackageTransfer.PlanExport(_workspace));

    /// <summary>Writes the ticked macros into the file the page's save dialog picked.</summary>
    private Task<object?> ExportApply(JsonElement parameters)
    {
        var path = RequiredPath(parameters);
        var names = Names(parameters);
        try
        {
            return Task.FromResult<object?>(PackageTransfer.ApplyExport(_workspace, path, names));
        }
        catch (BridgeFailureException)
        {
            throw;
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
    /// What a package would bring, before any of it is written. A package that cannot be read is
    /// named to the page with the same words the window list uses; the project stays as it was.
    /// </summary>
    private Task<object?> ImportPlan(JsonElement parameters)
    {
        var path = RequiredPath(parameters);
        try
        {
            return Task.FromResult<object?>(PackageTransfer.PlanImport(_workspace, path));
        }
        catch (Exception error)
        {
            throw new BridgeFailureException(BridgeErrors.PackageOpenFailed,
                Strings.Format("Bridge.PackageOpenFailed", Path.GetFileName(path), error.Message));
        }
    }

    /// <summary>
    /// Writes the ticked part of a package, with the answers the wizard collected about the global
    /// variables both sides declare. Everything that arrives lands in the project, and the page
    /// hears about it with the usual change event; the words come back with the state so the
    /// wizard can show the report and the list the same answer.
    /// </summary>
    private Task<object?> ImportApply(JsonElement parameters)
    {
        var path = RequiredPath(parameters);
        var names = Names(parameters);
        var decisions = Decisions(parameters);
        try
        {
            var report = PackageTransfer.ApplyImport(_workspace, path, names, decisions);
            return Task.FromResult<object?>(new ImportResult(report, State()));
        }
        catch (BridgeFailureException)
        {
            throw;
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

        var saved = await ProjectFiles.SaveAsync(_workspace, path);
        if (!saved.Ok)
        {
            throw new BridgeFailureException(BridgeErrors.PackageSaveFailed,
                Strings.Format("Bridge.PackageSaveFailed", Path.GetFileName(path),
                    saved.Error?.Message ?? string.Empty));
        }

        _rememberProject(path);
        await PushState();
        return null;
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
        var opened = await ProjectFiles.OpenAsync(_workspace, path);
        if (!opened.Ok)
        {
            throw new BridgeFailureException(BridgeErrors.PackageOpenFailed,
                Strings.Format("Bridge.PackageOpenFailed", Path.GetFileName(path),
                    opened.Error?.Message ?? string.Empty));
        }

        _rememberProject(path);
        await PushState();
    }

    /// <summary>Asks where a package goes; the answer is a path or "called off".</summary>
    private async Task<object?> PickSavePackageHandler(JsonElement parameters)
    {
        // The wizard names the file it is about to write; one field name for both the transfer
        // wizard and the editor's export.
        var suggested = Given(parameters, "suggested")
            ?? $"macros{MacroPackage.Extension}";
        return await PickSavePackage(suggested);
    }

    /// <summary>The package path a transfer needs, or a refusal that names what is missing.</summary>
    private static string RequiredPath(JsonElement parameters)
    {
        var path = Given(parameters, "path");
        if (path is not { Length: > 0 })
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.TransferNeedsPath"));
        }

        return path;
    }

    /// <summary>The macro names a transfer carries; a missing list is an empty one.</summary>
    private static IReadOnlyList<string> Names(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("names", out var names) || names.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. names.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()?.Trim() ?? string.Empty)
            .Where(name => name.Length > 0)];
    }

    /// <summary>The answers to the global conflicts, one per name.</summary>
    private static IReadOnlyList<ImportDecision> Decisions(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("decisions", out var decisions)
            || decisions.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var answers = new List<ImportDecision>();
        foreach (var item in decisions.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var name = Given(item, "name")?.Trim();
            var action = Given(item, "action")?.Trim();
            if (name is { Length: > 0 } && action is { Length: > 0 })
            {
                answers.Add(new ImportDecision(name, action));
            }
        }

        return answers;
    }

    private static string? Given(JsonElement node, string name)
        => node.TryGetProperty(name, out var given) && given.ValueKind == JsonValueKind.String
            ? given.GetString()
            : null;

    private async Task<object?> PickImage(JsonElement parameters)
        => new { path = await _files.OpenImageAsync() };

    private Task<string?> PickOpenPackage() => _files.OpenPackageAsync();

    private Task<string?> PickSavePackage(string suggestedName)
        => _files.SavePackageAsync(suggestedName);

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
