using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;
using WhaleGenie.Web;

namespace WhaleGenie.Bridge;

/// <summary>
/// Opens the native macro editor from the web shell. The editor stays a native window for now —
/// the screen pickers, the recording and the step tree live there — while the list is the page:
/// a save from the editor lands in the project, and the page hears about it through the same
/// <c>project.changed</c> every other change rides on.
/// </summary>
/// <remarks>
/// One editor at a time, exactly like the main window kept it: two editors on one macro would
/// each hand back their own version and the last one closed would win, so the editor already
/// open is activated instead. The save itself is <see cref="ProjectMacroCommit"/>, the same rule
/// the main window installed — the three things only the opener can do (stop the replaced run,
/// refresh the running dots, follow the crash draft after a save) are handed in here.
/// </remarks>
public sealed class EditorBridge : IBridgeHandler
{
    private readonly Func<MainViewModel> _project;

    private readonly TriggerRuntime _runtime;

    private readonly Func<WebShellWindow?> _shell;

    private MacroEditorWindow? _editor;

    internal EditorBridge(Func<MainViewModel> project, TriggerRuntime runtime, Func<WebShellWindow?> shell)
    {
        _project = project;
        _runtime = runtime;
        _shell = shell;
    }

    public void Register(BridgeServer server)
    {
        server.Register("editor.newMacro", NewMacro);
        server.Register("editor.open", Open);
    }

    private Task<object?> NewMacro(JsonElement parameters)
    {
        var view = parameters.TryGetProperty("view", out var named) && named.GetString() is { } text
            && Enum.TryParse<MacroView>(text, ignoreCase: true, out var parsed)
            ? parsed
            : (MacroView?)null;
        return Answer(OpenEditor(_project(), null, null, view));
    }

    private Task<object?> Open(JsonElement parameters)
    {
        var wanted = parameters.TryGetProperty("name", out var named) ? named.GetString() : null;
        var macro = _project().Macros.FirstOrDefault(candidate =>
            string.Equals(candidate.Name.Trim(), wanted, StringComparison.OrdinalIgnoreCase));
        if (macro is null)
        {
            throw new BridgeFailureException(BridgeErrors.NoSuchMacro, $"There is no macro named \"{wanted}\".");
        }

        return Answer(OpenEditor(_project(), macro, macro, null));
    }

    private Task<object?> Answer(MacroEditorWindow? editor)
        => Task.FromResult<object?>(new { opened = editor is not null });

    private MacroEditorWindow? OpenEditor(MainViewModel project, MacroItem? editing, MacroItem? existing,
        MacroView? newView)
    {
        if (_shell() is not { } shell)
        {
            // The shell is not the window on duty (or this is a headless check): nothing to be a
            // peer of, and a native editor opened without one would have no way back.
            return null;
        }

        if (_editor is { } open)
        {
            open.Activate();
            return open;
        }

        var editor = new MacroEditorWindow(editing, [.. project.Macros], project.CurrentPath,
            SharedDevices.Current, project.Triggers, callableOnly: false, newView, project.Persisted);
        var commit = new ProjectMacroCommit(project, existing, newView ?? MacroView.All,
            macro => _runtime.Triggers.Stop(macro.Name),
            () => { },
            () => { });
        _editor = editor;
        editor.Commit = draft => commit.Apply(draft, editor.LiftedMacros);
        editor.Closed += (_, _) =>
        {
            _editor = null;
            if (editor.Result is { } draft)
            {
                commit.Apply(draft, editor.LiftedMacros);
            }
        };

        // The editor stands beside the shell rather than replacing it, so both stay on screen and
        // either can be minimized without taking the other down.
        editor.ShowAsPeer(shell);
        return editor;
    }
}
