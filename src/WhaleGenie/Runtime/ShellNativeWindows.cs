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
/// The native windows the web shell still opens: the pickers, the icon and macro choosers, and the
/// import/export flows. The pages themselves are web; the shell owns the window, so these stand
/// beside it and the menu's actions route back through the page the same way the bridge commands do.
/// </summary>
internal sealed class ShellNativeWindows(
    ProjectWorkspace workspace,
    MainViewModel viewModel,
    Func<Window?> owner,
    Func<MacroItem, bool> openEditor,
    Action<MacroItem> openRun,
    Action openSettings) : INativeSurfaceActions, IDisposable
{
    /// <summary>The trigger host, once the shell has built it; settings republishes the plan.</summary>
    internal TriggerHost? Host { get; set; }

    public void OpenEditor(MacroItem macro) => openEditor(macro);

    public void OpenSettings(WindowInfo? foreground) => openSettings();

    public void OpenRun(MacroItem macro) => openRun(macro);

    /// <summary>
    /// Settings live in the page now; after one changes, the trigger plan and the hints that read
    /// the same values have to be rebuilt without waiting for a native window to close.
    /// </summary>
    internal void RefreshAfterSettings()
    {
        Host?.PublishPlan();
        viewModel.RefreshHotkeyHints();
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
    }
}
