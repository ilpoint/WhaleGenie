using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Models;

namespace WhaleGenie.Mcp;

/// <summary>
/// The pure validation rules of a draft. They take macro and global snapshots, never a draft lock,
/// a project, a file or the clock, so a caller can validate a candidate before touching a session.
/// </summary>
public static class McpDraftRules
{
    public static McpDraftValidation Validate(IReadOnlyList<MacroItem> macros,
        IReadOnlyList<VariableDefinition> globals)
    {
        var blocking = new List<McpProblem>();
        var hints = new List<McpProblem>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var macro in macros)
        {
            var name = macro.Name.Trim();
            if (name.Length == 0)
            {
                blocking.Add(new McpProblem(string.Empty, null, "MacroNameMissing",
                    "A macro has no name."));
                continue;
            }

            if (!names.Add(name))
            {
                blocking.Add(new McpProblem(name, null, "MacroNameDuplicate",
                    $"Two macros are named \"{name}\"."));
            }
        }

        var callables = macros.Select(macro => macro.Callable).ToList();
        foreach (var macro in macros)
        {
            foreach (var fault in MacroCheck.DeclarationFaultProblems(macro.Callable))
            {
                blocking.Add(new McpProblem(macro.Name, null, fault.Code, fault.Message));
            }

            foreach (var warning in MacroCheck.DeclarationWarnings(macro.Callable))
            {
                hints.Add(new McpProblem(macro.Name, null, "DeclarationWarning", warning));
            }

            foreach (var problem in MacroCheck.Inspect(macro.Steps, macro.Callable, callables,
                         globals))
            {
                var written = new McpProblem(macro.Name,
                    problem.Step.Id.Length > 0 ? problem.Step.Id : null,
                    problem.Code, problem.Message);

                (problem.Severity == MacroProblemSeverity.Blocking ? blocking : hints).Add(written);
            }
        }

        return new McpDraftValidation(blocking.Count == 0, blocking, hints);
    }
}
