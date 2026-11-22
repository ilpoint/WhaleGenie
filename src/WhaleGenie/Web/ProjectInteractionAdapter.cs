using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using WhaleGenie.Bridge;
using WhaleGenie.Contracts;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Projects;

namespace WhaleGenie.Web;

/// <summary>The native import/export choosers behind the project bridge's port.</summary>
internal sealed class ProjectInteractionAdapter(
    ProjectWorkspace workspace, Func<Window?> owner, DialogBridge dialogs)
    : IProjectInteraction
{
    public Task<ProjectOperationFailure?> ImportAsync()
        => RunAsync(interaction => interaction.ImportAsync());

    public Task<ProjectOperationFailure?> ExportAsync(IReadOnlyList<string> macroNames)
        => RunAsync(async interaction =>
        {
            var macros = macroNames
                .Select(name => workspace.Macros.FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, name, StringComparison.Ordinal)))
                .OfType<MacroItem>()
                .ToList();
            if (macros.Count > 0)
            {
                await interaction.ExportAsync(macros);
            }
        });

    private async Task<ProjectOperationFailure?> RunAsync(Func<ProjectInteraction, Task> action)
    {
        if (owner() is not { } window)
        {
            return null;
        }

        var interaction = new ProjectInteraction(workspace, () => window, ReportAsync);
        try
        {
            await action(interaction);
            return null;
        }
        catch (ProjectInteractionException error)
        {
            return new ProjectOperationFailure(error.Path,
                error.InnerException?.Message ?? error.Message);
        }
    }

    private Task ReportAsync(string title, string message)
        => dialogs.ConfirmAsync(title, message, Strings.Get("Common.Ok"), null);
}
