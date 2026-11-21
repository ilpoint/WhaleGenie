using System;
using System.Collections.Specialized;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;

namespace WhaleGenie.Bridge;

/// <summary>
/// The macro list's side of the bridge: the whole project state in one envelope, and the two
/// commands the list itself can make — arming or disarming a macro, and the system switch. The
/// page filters, searches and groups locally; the program remains the only one who knows what a
/// macro is and which view it belongs to.
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

    private BridgeServer? _server;

    internal ProjectBridge(ProjectWorkspace workspace, TriggerRuntime runtime)
    {
        _workspace = workspace;
        _runtime = runtime;
    }

    public void Register(BridgeServer server)
    {
        _server = server;
        server.Register("project.state", _ => Task.FromResult<object?>(State()));
        server.Register("project.toggle", Toggle);
        server.Register("project.system", SystemSwitch);

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
        return Task.FromResult<object?>(null);
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
