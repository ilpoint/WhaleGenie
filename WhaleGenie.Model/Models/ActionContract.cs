using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;

namespace WhaleGenie.Models;

/// <summary>
/// One disagreement between the two catalogues, located well enough to fix: which action, which
/// parameter or result inside it, and what is wrong.
///
/// The two files answer different questions — the engine's semantics say what a step does, the
/// display file says what a person reads — and every action needs both halves. A missing half used to
/// stop the program at the first one it met, which says which line to fix only if that line happens
/// to be the first; the report says all of them.
/// </summary>
public sealed record ActionContractIssue(string Kind, string Action, string? Field, string Detail)
{
    /// <summary>How the issue reads in a log or a failure message.</summary>
    public override string ToString()
        => Field is { Length: > 0 }
            ? $"{Kind} {Action}.{Field}: {Detail}"
            : $"{Kind} {Action}: {Detail}";
}

/// <summary>One parameter as the display file names it: the name and the editor kind it asks for.</summary>
public sealed record ActionContractParameter(string Name, string Kind);

/// <summary>One action as the display file has it, cut down to what a contract check reads.</summary>
public sealed record ActionContractDisplay(
    string Key, IReadOnlyList<ActionContractParameter> Parameters, IReadOnlyList<string> Returns);

/// <summary>
/// What the engine's catalogue and the display catalogue say about each other. Everything here is
/// about the two lists agreeing; how a label reads is nobody's business but the reader's.
/// </summary>
public static class ActionContract
{
    /// <summary>Every way the two lists disagree, in the order the display file would be read.</summary>
    public static IReadOnlyList<ActionContractIssue> Check(
        IReadOnlyList<ActionSemantics> semantics, IReadOnlyList<ActionContractDisplay> display)
    {
        var issues = new List<ActionContractIssue>();
        var byKey = new Dictionary<string, ActionSemantics>(StringComparer.Ordinal);
        foreach (var action in semantics)
        {
            if (!byKey.TryAdd(action.Key, action))
            {
                issues.Add(new ActionContractIssue("DuplicateAction", action.Key, null,
                    "the engine's catalogue has this action key twice"));
            }
        }

        var shownKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var shown in display)
        {
            if (!shownKeys.Add(shown.Key))
            {
                issues.Add(new ActionContractIssue("DuplicateAction", shown.Key, null,
                    "the display catalogue has this action key twice"));
                continue;
            }

            if (!byKey.TryGetValue(shown.Key, out var facts))
            {
                // A display entry with nothing behind it is a row a person can read and a step that
                // cannot exist: it shows up in the picker and fails when it is added.
                issues.Add(new ActionContractIssue("OrphanAction", shown.Key, null,
                    "the display catalogue has it and the engine's catalogue does not"));
                continue;
            }

            CheckParameters(facts, shown, issues);
            CheckReturns(facts, shown, issues);
        }

        foreach (var facts in semantics.Where(action => !shownKeys.Contains(action.Key)))
        {
            issues.Add(new ActionContractIssue("MissingAction", facts.Key, null,
                "the engine's catalogue has it and the display catalogue does not"));
        }

        return issues;
    }

    private static void CheckParameters(ActionSemantics facts, ActionContractDisplay shown,
        List<ActionContractIssue> issues)
    {
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in facts.Parameters)
        {
            known.Add(parameter.Name);
        }

        var shownNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in shown.Parameters)
        {
            if (!shownNames.Add(parameter.Name))
            {
                issues.Add(new ActionContractIssue("DuplicateParameter", facts.Key, parameter.Name,
                    "the display catalogue names this parameter twice"));
                continue;
            }

            if (!known.Contains(parameter.Name))
            {
                issues.Add(new ActionContractIssue("OrphanParameter", facts.Key, parameter.Name,
                    "the display catalogue has it and the engine's catalogue does not"));
                continue;
            }

            // The kind decides which control the form draws, so a name that parses to nothing is a
            // field the editor cannot lay out at all — said here rather than at the first click.
            if (!Enum.TryParse<ActionParameterKind>(parameter.Kind, ignoreCase: true, out _))
            {
                issues.Add(new ActionContractIssue("UnknownKind", facts.Key, parameter.Name,
                    $"the display catalogue asks for the editor kind \"{parameter.Kind}\", "
                    + "which the editor does not have"));
            }
        }

        issues.AddRange(facts.Parameters
            .Where(parameter => !shownNames.Contains(parameter.Name))
            .Select(parameter => new ActionContractIssue("MissingParameter", facts.Key,
                parameter.Name, "the engine's catalogue has it and the display catalogue does not")));
    }

    private static void CheckReturns(ActionSemantics facts, ActionContractDisplay shown,
        List<ActionContractIssue> issues)
    {
        var known = new HashSet<string>(facts.Returns.Select(result => result.Key),
            StringComparer.Ordinal);
        var shownKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var result in shown.Returns)
        {
            if (!shownKeys.Add(result))
            {
                issues.Add(new ActionContractIssue("DuplicateReturn", facts.Key, result,
                    "the display catalogue names this result twice"));
                continue;
            }

            if (!known.Contains(result))
            {
                issues.Add(new ActionContractIssue("OrphanReturn", facts.Key, result,
                    "the display catalogue has it and the engine's catalogue does not"));
            }
        }

        issues.AddRange(facts.Returns
            .Where(result => !shownKeys.Contains(result.Key))
            .Select(result => new ActionContractIssue("MissingReturn", facts.Key, result.Key,
                "the engine's catalogue has it and the display catalogue does not")));
    }
}
