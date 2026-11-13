using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.Localization;

namespace WhaleGenie.Models;

/// <summary>
/// Reading the steps rather than judging them: which names one parameter reads, which names the
/// steps define, and what kind each of those names is known to hold. The checks ask these
/// questions while walking a macro; nothing here reports a problem of its own.
/// </summary>
public static partial class MacroCheck
{
    /// <summary>
    /// The variable names one parameter reads, taken from the value it holds rather than from the
    /// line it is written as. A whole value may name several variables anywhere inside it, a run of
    /// text names them where they stand, and a field that only names a variable reads that one.
    /// A field that writes a name reads nothing.
    /// </summary>
    public static IReadOnlyList<string> ReadsOf(MacroStep step, StepParameter parameter)
    {
        if (step.DefinesVariable(parameter))
        {
            return [];
        }

        // A list parameter keeps its values in rows, and every one of them may name a picture an
        // earlier step took or a value an expression works out.
        if (parameter.IsRowList)
        {
            return [.. parameter.RowValues
                .SelectMany(Expression.References)
                .Distinct(StringComparer.OrdinalIgnoreCase)];
        }

        var definition = step.ParameterDefinition(parameter);
        if (definition is null)
        {
            // A macro call's arguments are not in the catalogue; the file reads them as one value.
            return Expression.References(parameter.Written);
        }

        // A field that names a variable holds the name as the word it is, and reading it means
        // looking that name up; a field written as something computed names whatever it computes.
        if (definition.ValueForm is ActionValueForm.Name)
        {
            return parameter.Written is TextNode text && text.Text.Trim() is { Length: > 0 } name
                ? [name]
                : Expression.References(parameter.Written);
        }

        return Expression.References(parameter.Written);
    }

    /// <summary>A yes/no field the way the engine reads one: anything but "false" is on.</summary>
    private static bool On(MacroStep step, string name)
    {
        var text = step.Text(name).Trim();
        return text.Length > 0
            && !string.Equals(text, "false", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The names a loop writes for its own rounds: the item and round number of a "for each" and
    /// the counter of a counting loop. A repeat or a while counts the rounds in the runner's head,
    /// so there is no name of the macro's to keep.
    /// </summary>
    private static string[] LoopVariableNames(MacroStep step)
    {
        switch (step.Type)
        {
            case "control.forEach":
                var item = step.Text("itemVariable").Trim();
                var index = step.Text("indexVariable").Trim();
                return
                [
                    .. new[] { item.Length == 0 ? "item" : item, index }
                        .Where(name => name.Length > 0),
                ];
            case "control.for":
                var counter = step.Text("name").Trim();
                return [counter.Length == 0 ? "i" : counter];
            default:
                return [];
        }
    }
    /// <summary>
    /// The variable a run-handle field names when it holds one variable and nothing else, or null
    /// when it is an expression. Only that form lets the check say which run a wait settles.
    /// </summary>
    /// <summary>    /// <summary>
    /// The names a macro may read: the ones WhaleGenie provides, the shared ones, the ones its own
    /// steps create, and the ones a call asks a called macro to bring back. A name the macro
    /// defines anywhere counts, because whether a step runs before the one that reads it is a
    /// question the steps alone cannot answer.
    /// </summary>
    /// <summary>
    /// The identities a macro may read: the shared declarations, its own parameters, results and
    /// locals, and the names its steps declare. A reference carrying an identity outside this set
    /// points at a declaration that is gone or was never here.
    /// </summary>
    private static HashSet<string> DefinedIds(IReadOnlyList<MacroStep> steps, MacroCallable? own)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var global in VariableCatalog.Globals.Where(global => global.Id.Length > 0))
        {
            ids.Add(global.Id);
        }

        if (own is not null)
        {
            foreach (var parameter in own.Parameters.Where(parameter => parameter.Id.Length > 0))
            {
                ids.Add(parameter.Id);
            }

            foreach (var result in own.Results.Where(result => result.Id.Length > 0))
            {
                ids.Add(result.Id);
            }

            foreach (var local in own.OwnLocals.Where(local => local.Id.Length > 0))
            {
                ids.Add(local.Id);
            }
        }

        foreach (var step in Every(steps))
        {
            foreach (var parameter in step.Parameters)
            {
                if (parameter.Kind is ActionParameterKind.Returns)
                {
                    foreach (var row in parameter.Rows)
                    {
                        var id = Formula.NameOf(row.Cell(FieldNames.Name)).Id;
                        if (id.Length > 0)
                        {
                            ids.Add(id);
                        }
                    }
                }
                else if (step.DefinesVariable(parameter))
                {
                    var id = Formula.NameOf(parameter.Written).Id;
                    if (id.Length > 0)
                    {
                        ids.Add(id);
                    }
                }
            }
        }

        return ids;
    }

    private static HashSet<string> DefinedVariables(IReadOnlyList<MacroStep> steps)
    {
        var defined = new HashSet<string>(
            VariableCatalog.SystemVariables.Select(variable => variable.Name),
            StringComparer.OrdinalIgnoreCase);
        defined.UnionWith(VariableCatalog.Globals.Select(variable => variable.Name));

        // A catch block is handed the failure it took on under these two names. Whether a read
        // sits inside a catch is not something the walk below can settle yet, so the names count
        // as known rather than every correct catchWhen being marked as reading nothing.
        defined.Add(MacroRunner.CaughtKeyName);
        defined.Add(MacroRunner.CaughtDetailName);

        foreach (var step in steps)
        {
            step.CollectVariables(defined);
        }

        foreach (var step in Every(steps).Where(step => step.Type == "control.runMacro"))
        {
            foreach (var (_, name) in Wanted(step))
            {
                defined.Add(name);
            }
        }

        // Every step that can run leaves an ending and a failure key behind under its own id,
        // whether or not the editor asked for a named result.
        foreach (var step in Every(steps).Where(step => step.Id.Length > 0))
        {
            defined.Add(MacroRunner.OutcomeName(step.Id));
        }

        return defined;
    }

    /// <summary>
    /// The results a call asks to have back, one row per result: what the called macro calls the
    /// value, and the name the caller wants it under. A row without both is not asking for anything.
    /// </summary>
    /// <summary>
    /// What kind of value each name the macro can read is known to hold. Only what the steps and
    /// declarations say: a result the catalogue calls a record is a record, a line of writing is
    /// text, and a name nothing declares is left unknown rather than guessed at.
    /// </summary>
    private static Dictionary<string, ValueKind> KnownTypes(IReadOnlyList<MacroStep> steps,
        MacroCallable? own, Dictionary<string, MacroCallable?> reachable)
    {
        var known = new Dictionary<string, ValueKind>(StringComparer.OrdinalIgnoreCase);
        foreach (var variable in VariableCatalog.SystemVariables)
        {
            known[variable.Name] = variable.Type;
        }

        if (own is not null)
        {
            foreach (var parameter in own.Parameters)
            {
                if (parameter.Name.Trim() is { Length: > 0 } name && VariableName.IsName(name))
                {
                    known[name] = parameter.Type;
                }
            }

            // An in-out name keeps the parameter's kind: before it is written it holds the value
            // the caller passed, and that is what a read at the top of the macro sees.
            foreach (var result in own.Results)
            {
                if (result.Name.Trim() is { Length: > 0 } name
                    && VariableName.IsName(name)
                    && !known.ContainsKey(name))
                {
                    known[name] = result.Type;
                }
            }

            // A declared local holds its kind from before the first step, so a formula reading one
            // is checked against the kind it was declared as rather than left unknown.
            foreach (var local in own.OwnLocals)
            {
                if (local.Name.Trim() is { Length: > 0 } name
                    && VariableName.IsName(name)
                    && !known.ContainsKey(name))
                {
                    known[name] = local.Type;
                }
            }
        }

        foreach (var step in Every(steps))
        {
            if (step.Definition is { } definition
                && step.Parameters.FirstOrDefault(parameter =>
                    parameter.Kind is ActionParameterKind.Returns) is { } returns)
            {
                foreach (var (key, name) in step.ReturnedResults(returns))
                {
                    if (definition.Returns.FirstOrDefault(result =>
                            string.Equals(result.Key, key, StringComparison.OrdinalIgnoreCase))
                        is { } result)
                    {
                        known[name] = result.Type;
                    }
                }
            }

            if (CallsMacro(step)
                && reachable.GetValueOrDefault(step.Text("macro").Trim()) is { } called)
            {
                foreach (var (result, name) in Wanted(step))
                {
                    if (called.Results.FirstOrDefault(candidate => string.Equals(candidate.Name,
                            result, StringComparison.OrdinalIgnoreCase)) is { } declared)
                    {
                        known[name] = declared.Type;
                    }
                }
            }
        }

        return known;
    }

    /// <summary>
    /// The fields each record this macro can read declares, by variable name. Only a record whose
    /// author wrote its fields down appears: a record that says nothing is one whose fields are
    /// whatever the value happens to hold, and a check against nothing would be a guess.
    /// </summary>
    private static Dictionary<string, IReadOnlyList<string>> KnownFields(
        IReadOnlyList<MacroStep> steps, MacroCallable? own,
        Dictionary<string, MacroCallable?> reachable)
    {
        var fields = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        void Add(string name, ValueKind type, IEnumerable<MacroField> declared)
        {
            if (!VariableName.IsName(name) || fields.ContainsKey(name))
            {
                return;
            }

            var named = declared.Select(field => field.Name.Trim())
                .Where(field => field.Length > 0)
                .ToList();
            if (type == ValueKind.Record && named.Count > 0)
            {
                fields[name] = named;
            }
        }

        if (own is not null)
        {
            foreach (var parameter in own.Parameters)
            {
                Add(parameter.Name.Trim(), parameter.Type, parameter.Fields);
            }

            foreach (var result in own.Results)
            {
                Add(result.Name.Trim(), result.Type, result.Fields);
            }
        }

        // A record that arrives from a call belongs to the called macro's declaration, so its
        // fields come from there; the call's own rows say where the value lands, not what is in it.
        foreach (var step in Every(steps))
        {
            if (!CallsMacro(step)
                || reachable.GetValueOrDefault(step.Text("macro").Trim()) is not { } called)
            {
                continue;
            }

            foreach (var (result, name) in Wanted(step))
            {
                if (called.Results.FirstOrDefault(candidate => string.Equals(candidate.Name,
                        result, StringComparison.OrdinalIgnoreCase)) is { } declared)
                {
                    Add(name, declared.Type, declared.Fields);
                }
            }
        }

        return fields;
    }

    /// <summary>
    /// True when a value of one kind cannot stand for a value the called macro declared: a record
    /// is not a line of text, a line of text is not a record, and a list is not a number.
    /// </summary>
    private static bool ObviouslyWrong(ValueKind declared, ValueKind actual)
    {
        if (declared == actual)
        {
            return false;
        }

        var composite = actual
            is ValueKind.Record or ValueKind.List or ValueKind.Image;
        return declared switch
        {
            ValueKind.Record => !composite,
            ValueKind.Text => composite,
            ValueKind.Number or ValueKind.Bool => composite,
            _ => false,
        };
    }

    /// <summary>
    /// The name a cell holds when it is one variable and nothing else, or null when it is text, an
    /// expression or several things at once.
    /// </summary>
    private static string? WholeName(string value)
    {
        var text = value.Trim();
        if (text.Length < 2 || text[0] != '$')
        {
            return null;
        }

        var name = text[1..];
        return VariableName.IsName(name) ? name : null;
    }

    private static IEnumerable<(string Result, string Name)> Wanted(MacroStep step)
    {
        foreach (var parameter in step.Parameters.Where(parameter => parameter.Name == "returns"))
        {
            foreach (var row in parameter.Rows)
            {
                var result = row.Text(FieldNames.Key).Trim();
                var name = Formula.NameOf(row.Cell(FieldNames.Name)).Name.Trim();
                if (result.Length > 0 && name.Length > 0)
                {
                    yield return (result, name);
                }
            }
        }
    }

    /// <summary>Every step of the macro, nested ones and conditions included.</summary>
    private static IEnumerable<MacroStep> Every(IEnumerable<MacroStep> steps)
    {
        foreach (var step in steps)
        {
            yield return step;

            foreach (var parameter in step.Parameters)
            {
                foreach (var nested in Every(parameter.Steps))
                {
                    yield return nested;
                }

            }
        }
    }

    /// <summary>
    /// Every variable name the steps read, nested steps included. It is what says which values a
    /// stretch of steps takes from around it: the values a lift has to turn into arguments of the
    /// macro it makes, and what "nothing defines this" is decided against elsewhere.
    /// </summary>
    /// <summary>
    /// What kind of value each name the steps can read is known to hold — the same answer the checks
    /// work from, offered to the editor. A macro lifted out of a stretch of steps declares the
    /// arguments it now takes; without this it would call every one of them text, and the call site
    /// would then say a number is being passed where text is expected.
    /// </summary>
    public static IReadOnlyDictionary<string, ValueKind> TypesOf(IEnumerable<MacroStep> steps,
        MacroCallable? own, IEnumerable<MacroCallable> macros)
    {
        IReadOnlyList<MacroStep> tree = steps as IReadOnlyList<MacroStep> ?? [.. steps];
        var reachable = new Dictionary<string, MacroCallable?>(StringComparer.OrdinalIgnoreCase);
        foreach (var macro in macros)
        {
            reachable[macro.Name.Trim()] = macro;
        }

        if (own?.Name.Trim() is { Length: > 0 } name)
        {
            reachable[name] = own;
        }

        return KnownTypes(tree, own, reachable);
    }

    public static IReadOnlyList<string> Reads(IEnumerable<MacroStep> steps)
    {
        var names = new List<string>();
        foreach (var step in Every(steps))
        {
            foreach (var parameter in step.Parameters)
            {
                if (step.ParameterDefinition(parameter) is { ForeignText: true })
                {
                    continue;
                }

                names.AddRange(ReadsOf(step, parameter));
            }
        }

        return names;
    }
}
