using Avalonia.Controls;

namespace WhaleGenie.Web;

/// <summary>The window operations the bridge's port asks for, on the real shell window.</summary>
internal sealed class ShellWindowCommands(WebShellWindow window)
    : WhaleGenie.Contracts.IWindowCommands
{
    public void Minimize() => window.WindowState = WindowState.Minimized;

    public void ToggleMaximize()
        => window.WindowState = window.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    public void Close() => window.Close();

    public void TogglePin() => window.Topmost = !window.Topmost;

    public void BeginDrag() => window.BeginNativeDrag();

    public void SetTextInputFocused(bool focused) => window.SetTextInputFocused(focused);
}
