using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Projects;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Runtime;

/// <summary>
/// The native windows the web shell still opens while its pages are being moved: the macro editor,
/// settings and run-and-debug. The shell owns the window, so these stand beside it rather than
/// beside a hidden main window; the menu's actions and the temporary bridge commands both land here.
/// </summary>
internal sealed class ShellNativeWindows(
    ProjectWorkspace workspace,
    MainViewModel viewModel,
    TriggerRuntime runtime,
    Func<Window?> owner,
    Func<MacroItem, bool> openEditor,
    Action<MacroItem> openRun) : INativeSurfaceActions, IDisposable
{
    private AdvancedSettingsWindow? _settings;
    private RunWindow? _run;

    /// <summary>The trigger host, once the shell has built it; settings republishes the plan.</summary>
    internal TriggerHost? Host { get; set; }

    public void OpenEditor(MacroItem macro) => openEditor(macro);

    public void OpenSettings(WindowInfo? foreground) => _ = OpenSettingsAsync();

    public void OpenRun(MacroItem macro) => openRun(macro);

    internal Task OpenSettingsAsync(SettingsSubPage page = SettingsSubPage.General)
    {
        if (owner() is not { } window)
        {
            return Task.CompletedTask;
        }

        if (_settings is { } open)
        {
            open.SelectPage(page);
            open.Activate();
            return Task.CompletedTask;
        }

        var dialog = new AdvancedSettingsWindow(viewModel, workspace.CurrentPath, page);
        _settings = dialog;
        dialog.Closed += (_, _) =>
        {
            if (ReferenceEquals(_settings, dialog))
            {
                _settings = null;
            }

            // Everything the setting pages change is read back through these three: the runtime
            // plan the triggers run from, the words on the hotkey hints, and the list order.
            Host?.PublishPlan();
            viewModel.RefreshHotkeyHints();
            viewModel.RefreshVisibleMacros();
        };
        dialog.ShowAsPeer(window);
        dialog.Activate();
        return Task.CompletedTask;
    }

    internal async Task OpenRunAsync(MacroItem? macro)
    {
        if (owner() is not { } window)
        {
            return;
        }

        if (_run is { } open)
        {
            open.BringBack();
            if (macro is not null)
            {
                await open.DebugAsync(macro);
            }

            return;
        }

        var before = viewModel.Globals();
        var run = new RunWindow([], runtime.Devices, listed: [.. workspace.Macros],
            triggers: runtime.Triggers, variables: workspace.Variables);
        _run = run;
        run.Closed += (_, _) =>
        {
            if (ReferenceEquals(_run, run))
            {
                _run = null;
            }

            if (viewModel.GlobalsChangedSince(before))
            {
                viewModel.MarkGlobalsChanged();
            }
        };
        run.ShowAsPeer(window);
        run.Activate();
        if (macro is not null)
        {
            await run.DebugAsync(macro);
        }
    }

    internal async Task OpenAboutAsync()
    {
        if (owner() is not { } window)
        {
            return;
        }

        var dialog = new AboutWindow();
        var closing = dialog.ShowDialogOver(window);
        dialog.Activate();
        await closing;
    }

    internal static void OpenManual() => WebPage.Open(Manual.Where());

    /// <summary>
    /// Writes the project for the unsaved-work question. False means the picker was called off or
    /// the write failed; the session then keeps the program open.
    /// </summary>
    internal async Task<bool> SaveAsync()
    {
        if (owner() is not { } window)
        {
            return false;
        }

        var path = workspace.CurrentPath;
        if (path is not { Length: > 0 })
        {
            path = await PackagePicker.AskWhereAsync(window, Strings.Get("Main.SaveProjectTitle"),
                $"macros{MacroPackage.Extension}");
            if (path is not { Length: > 0 })
            {
                return false;
            }
        }

        var saved = ProjectFiles.Save(workspace, path);
        if (!saved.Ok)
        {
            await ConfirmDialog.ShowAsync(window, Strings.Get("Package.SaveFailed"),
                Path.GetFileName(path) + "\n" + saved.Error?.Message,
                Strings.Get("Common.Ok"), null, false);
            return false;
        }

        LocalSettings.StoreLastProject(path);
        return true;
    }

    internal void Restore(RecoveryContents contents) => workspace.RestoreFrom(contents);

    public void Dispose()
    {
        _settings?.Close();
        _settings = null;
        _run?.Close();
        _run = null;
    }
}
