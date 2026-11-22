namespace WhaleGenie.Web;

/// <summary>The native manual window behind the shell bridge's port.</summary>
internal sealed class ShellCommands : WhaleGenie.Contracts.IShellCommands
{
    public void OpenManual() => WhaleGenie.Runtime.ShellNativeWindows.OpenManual();
}
