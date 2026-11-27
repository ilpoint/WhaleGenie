using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using WhaleGenie.Bridge;
using WhaleGenie.Contracts;
using WhaleGenie.Editor;
using WhaleGenie.Localization;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;
using WindowState = Avalonia.Controls.WindowState;

namespace WhaleGenie.Web;

/// <summary>
/// Owns the one macro workbench window. It is a second shell page, not a second trigger host:
/// the window carries only the editor and run/debug pages, and the main window keeps the tray,
/// the project session and the menu/wheel/panel surfaces.
/// </summary>
public sealed class WorkbenchWindowHost(
    BridgeServer bridge,
    IEditorDocuments editor,
    EditorLeavePrompt prompt,
    ShellWindowRegistry registry) : IWorkbenchHost, IWindowCommands, IDisposable
{
    private WebShellWindow? _window;
    private bool _allowClose;
    private bool _prompting;
    private bool _disposed;

    public bool IsOpen => _window is { IsVisible: true };

    public void Show()
    {
        if (_disposed)
        {
            return;
        }

        if (_window is null)
        {
            _window = new WebShellWindow(bridge, hosts: null, surface: BridgeSurface.Workbench,
                registry: registry, workbenchWindow: true)
            {
                Width = 1400,
                Height = 900,
                MinWidth = 1100,
                MinHeight = 720,
            };
            _window.Closing += OnClosing;
            _window.Closed += OnClosed;
        }

        if (!_window.IsVisible)
        {
            _window.Show();
        }

        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();
    }

    public void OpenEditor(string? name)
    {
        Show();
        _window?.OpenEditor(name ?? string.Empty);
    }

    public void OpenRun(string name)
    {
        Show();
        _window?.OpenRun(name);
    }

    public void FocusMain()
    {
        var main = registry.Main;
        if (main is null)
        {
            return;
        }

        if (!main.IsVisible)
        {
            main.Show();
        }

        if (main.WindowState == WindowState.Minimized)
        {
            main.WindowState = WindowState.Normal;
        }

        main.Activate();
    }

    /// <summary>The window commands the page's title bar asks for.</summary>
    public void Minimize()
    {
        if (_window is { } window)
        {
            window.WindowState = WindowState.Minimized;
        }
    }

    public void ToggleMaximize()
    {
        if (_window is { } window)
        {
            window.WindowState = window.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }
    }

    public void Close() => _window?.Close();

    public void TogglePin()
    {
        if (_window is { } window)
        {
            window.Topmost = !window.Topmost;
        }
    }

    public void BeginDrag() => _window?.BeginNativeDrag();

    public void SetTextInputFocused(bool focused) => _window?.SetTextInputFocused(focused);

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose || e.Cancel)
        {
            return;
        }

        e.Cancel = true;
        if (_prompting)
        {
            return;
        }

        Dispatcher.UIThread.Post(() => _ = CloseAfterPromptAsync());
    }

    private async Task CloseAfterPromptAsync()
    {
        if (_window is not { } window || _prompting)
        {
            return;
        }

        _prompting = true;
        try
        {
            if (editor.HasUnsavedWork)
            {
                var answer = await prompt.AskAsync(editor.UnsavedDocuments);
                if (answer is null)
                {
                    if (!await ResolveNativeAsync(window))
                    {
                        CancelExit();
                        return;
                    }
                }
                else if (!answer.Value || editor.HasUnsavedWork)
                {
                    CancelExit();
                    return;
                }
            }

            await editor.CloseAllAsync();
            _allowClose = true;
            window.Close();
        }
        finally
        {
            _prompting = false;
        }
    }

    /// <summary>A cancelled close must also stop the tray's walk; a manual close has no walk to stop.</summary>
    private static void CancelExit()
    {
        if (AppTray.Current?.IsLeaving == true)
        {
            AppTray.Current.CalledOff();
        }
    }

    /// <summary>The page was not ready to draw the question; ask with the native dialog instead.</summary>
    private async Task<bool> ResolveNativeAsync(Window owner)
    {
        var names = string.Join(Environment.NewLine, editor.UnsavedDocuments);
        var choice = await ConfirmDialog.ShowAsync(owner,
            Strings.Get("Editor.UnsavedTitle"),
            Strings.Format("Editor.UnsavedExitMessage", names),
            Strings.Get("Editor.Save"), Strings.Get("Editor.Discard"), true, height: 300);
        switch (choice)
        {
            case ConfirmChoice.Primary:
                {
                    var result = await editor.SaveAllAsync();
                    if (result.Saved)
                    {
                        return true;
                    }

                    var detail = string.Join(Environment.NewLine, result.Blocked.Select(document =>
                        Strings.Format("Editor.UnsavedBlockedLine", document.Name, document.Problem)));
                    await ConfirmDialog.ShowAsync(owner,
                        Strings.Get("Editor.UnsavedBlockedTitle"), detail,
                        Strings.Get("Common.Ok"), null, showCancel: false, height: 300);
                    return false;
                }
            case ConfirmChoice.Secondary:
                await editor.DiscardAllAsync();
                return true;
            default:
                return false;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_window is { } window)
        {
            window.Closing -= OnClosing;
            window.Closed -= OnClosed;
        }

        _window = null;
        _allowClose = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _allowClose = true;
        if (_window is { } window)
        {
            window.Closing -= OnClosing;
            window.Closed -= OnClosed;
            window.Close();
        }

        _window = null;
    }
}
