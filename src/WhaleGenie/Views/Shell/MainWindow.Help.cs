using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;


namespace WhaleGenie.Views;

// The menu entries that open settings, the changes list, updates and the MCP page.
public partial class MainWindow : AppWindow
{
    private void OnSettingsClicked(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        OnOpenSettings(sender, e);
    }

    /// <summary>Shows what the whole release list says, which is the change log.</summary>
    private void OnWhatsNewClicked(object? sender, RoutedEventArgs e)
        => WebPage.Open(ProjectLinks.Releases);

    /// <summary>
    /// Opens where a newer version would be. WhaleGenie does not check by itself: asking GitHub on
    /// every launch is a network call nobody asked for, and opening the page leaves the choice —
    /// and the download — with the user.
    /// </summary>
    private void OnUpdatesClicked(object? sender, RoutedEventArgs e)
        => WebPage.Open(ProjectLinks.LatestRelease);

    private void OnFeedbackClicked(object? sender, RoutedEventArgs e)
        => WebPage.Open(ProjectLinks.Feedback);

    /// <summary>
    /// Opens the manual. "Get started" says what the item is about, and the manual is where the
    /// program explains itself: the copy that came with the program when there is one, the page in
    /// the repository otherwise.
    /// </summary>
    private void OnGetStartedClicked(object? sender, RoutedEventArgs e)
        => WebPage.Open(Manual.Where());

    /// <summary>
    /// Gives the flyout its window's data context. A flyout's content is not part of the window's
    /// visual tree, so the bindings that name the MCP view model need this one hand-off. The button
    /// only opens the flyout; the switch inside it owns the start and stop.
    /// </summary>
    private void OnMcpClicked(object? sender, RoutedEventArgs e)
    {
        if (this.FindControl<Button>("McpButton")?.Flyout is Flyout { Content: Control content })
        {
            content.DataContext = DataContext;
        }
    }

    /// <summary>
    /// Copies the block a client's configuration file wants: this program's path and the flag that
    /// makes it speak MCP. The client starts the process itself, so there is no address to paste.
    /// </summary>
    private async void OnCopyMcpConfig(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (viewModel.Mcp.Executable.Length == 0
            || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            viewModel.Mcp.ShowNoProgram();
            return;
        }

        await clipboard.SetTextAsync(viewModel.Mcp.Config);
        viewModel.Mcp.ShowCopied();
    }

    /// <summary>
    /// Opens the window that says what the program is, shows the two donation codes, and offers the
    /// project page and the list of third-party work. Both the "?" button and "About" open it.
    /// </summary>
    private async void OnAboutClicked(object? sender, RoutedEventArgs e)
        => await new AboutWindow().ShowDialogOver(this);

    private async void OnOpenSettings(object? sender, RoutedEventArgs e)
        => await OpenSettingsAsync(SettingsSubPage.General);

    /// <summary>
    /// Opens the settings window beside the main window, on the page asked for, and answers when it
    /// closes. One at a time: a second request brings the open window forward instead of laying
    /// another copy of the same settings over it.
    /// </summary>
    /// <remarks>
    /// It is shown as a peer rather than as a dialog because the pages it holds reach outside it: a
    /// picker has to put this program's windows away and take a window from the desk, and the wheel
    /// has to be tried while its page is open. A modal settings window cannot be put away that way
    /// without breaking the modal chain its owner is disabled by, which is what left "add rule"
    /// doing nothing at all.
    /// </remarks>
    private Task OpenSettingsAsync(SettingsSubPage page)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return Task.CompletedTask;
        }

        if (_settings is { } open)
        {
            open.Activate();
            return Task.CompletedTask;
        }

        var dialog = new SettingsWindow(viewModel, viewModel.CurrentPath, page);
        var closed = new TaskCompletionSource();
        _settings = dialog;
        dialog.Closed += (_, _) =>
        {
            if (ReferenceEquals(_settings, dialog))
            {
                _settings = null;
            }

            // Everything the settings pages change is read back through these three: the runtime
            // snapshot the triggers run from, the words on the hotkey hints, and the order of the
            // list, which the settings can change without touching a single macro.
            PublishRuntimeState(viewModel);
            viewModel.RefreshHotkeyHints();
            viewModel.RefreshVisibleMacros();
            closed.TrySetResult();
        };
        dialog.ShowAsPeer(this);
        return closed.Task;
    }

    private Task OpenMenuSettingsAsync(WindowInfo? foreground) => ShowMenuSettingsAsync(foreground);

    private async Task ShowMenuSettingsAsync(WindowInfo? foreground)
    {
        // The settings window is not modal any more, so this waits for it to close by its own
        // Closed event rather than by the modal loop that used to end here.
        await OpenSettingsAsync(SettingsSubPage.MenuLauncher);
        if (foreground is not null)
        {
            try
            {
                _devices.Windows.Activate(foreground.Handle);
            }
            catch
            {
                // The target may have closed while settings were open.
            }
        }
    }
}
