using System;
using System.Threading.Tasks;
using WhaleGenie.Projects;
using WhaleGenie.Web;

namespace WhaleGenie.Runtime;

/// <summary>
/// Asks the shell's project session before an open/reload/close-all replaces the document. The
/// bridge only knows the request; the session knows which editor documents and unsaved project
/// changes are at stake and how to draw the questions.
/// </summary>
internal sealed class ShellProjectChangeGuard(Func<WebShellWindow?> window) : IProjectChangeGuard
{
    public Task<bool> ConfirmAsync(ProjectChangeKind kind)
        => window()?.Session is { } session
            ? session.PrepareProjectChangeAsync(kind)
            : Task.FromResult(true);
}
