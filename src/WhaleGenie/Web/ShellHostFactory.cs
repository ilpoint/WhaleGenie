using WhaleGenie.Bridge;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Web;

/// <summary>
/// Builds the session the web shell carries: the native surfaces, the trigger host and the native
/// windows the pages have not taken over. It is a factory because the owner window does not exist
/// when the container is built; the shell asks for its host once it is opening.
/// </summary>
internal sealed class ShellHostFactory(
    ProjectWorkspace workspace,
    SystemState system,
    TriggerRuntime runtime,
    RunQuestionHost questions,
    MainViewModel viewModel)
{
    public ShellHost Create(WebShellWindow owner)
    {
        var actions = new ShellNativeWindows(workspace, viewModel, runtime, () => owner,
            macro =>
            {
                owner.OpenEditor(macro.Name);
                return true;
            },
            macro => owner.OpenRun(macro.Name));
        return new ShellHost(workspace, system, runtime, questions, actions, owner, owner);
    }
}
