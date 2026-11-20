using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// What a step must be given before it runs, and what it promises to hand back. Both questions are
/// answered from the catalogue rather than from a list kept here, so an action that is added to the
/// catalogue is checked the moment it is written, and an answer that does not fit the declared kind
/// is refused before it can be mistaken for a result the macro meant.
/// </summary>
internal static class StepChecks
{
    /// <summary>
    /// What the catalogue says a step must be given, checked before the step runs. An unanswered
    /// field is a mistake to report rather than an empty value for the action to interpret; a word
    /// outside the choices is refused unless the field says an unknown word falls back. Both use
    /// the key the catalogue names for that field, so a path still says "no path" rather than the
    /// general word for an unanswered field.
    /// </summary>
    public static void ValidateAnswers(ExecutableStep step)
    {
        if (ActionSemanticsCatalog.Find(step.Type) is not { } definition)
        {
            return;
        }

        foreach (var facts in definition.Parameters)
        {
            var written = step.Text(facts.Name).Trim();
            if (facts.MissingValue(written))
            {
                throw new StepFailure(facts.MissingKey, facts.Name);
            }

            if (facts.Shape is ActionValueShape.Rows && facts.Columns.Count > 0)
            {
                CheckRowColumns(facts, step.Rows(facts.Name));
            }
            if (facts.Shape is ActionValueShape.Choice
                && written.Length > 0
                && facts.Options.Count > 0
                && !facts.Options.Contains(written, StringComparer.Ordinal)
                && !facts.AllowsUnknownOption)
            {
                throw new StepFailure(facts.OptionKey, written);
            }
        }

        ValidateReturnRequests(step, definition);
    }

    /// <summary>
    /// A return request is part of the step, so a hand-written key, destination name or repeated
    /// answer that the editor would not produce is refused before the step can do any work. Calls
    /// to another macro check their keys against that macro's signature when the call is resolved.
    /// </summary>
    private static void ValidateReturnRequests(ExecutableStep step, ActionSemantics definition)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in step.Rows("returns"))
        {
            var key = ShapeWords.Of(row.GetValueOrDefault(FieldNames.Key));
            var name = row.TryGetValue(FieldNames.Name, out var writtenName)
                ? Formula.NameOf(writtenName)
                : string.Empty;
            if (key.Length == 0 || name.Length == 0)
            {
                continue;
            }

            if (!VariableName.IsName(name) || !keys.Add(key) || !names.Add(name))
            {
                throw new StepFailure(RunKeys.BadResult, name);
            }

            if (definition.SignatureSource is not ActionSignatureSource.CalledMacro
                && definition.Return(key) is null)
            {
                throw new StepFailure(RunKeys.BadResult, key);
            }
        }
    }

    /// <summary>
    /// A row list is checked against the columns its field declares. A column nobody reads would be
    /// skipped in silence, which looks like the action ignoring what the row said; one the field
    /// requires but the row leaves out is a row that cannot say what it was meant to say.
    /// </summary>
    private static void CheckRowColumns(ActionParameterSemantics facts,
        IReadOnlyList<IReadOnlyDictionary<string, ExpressionNode>> rows)
    {
        foreach (var row in rows)
        {
            foreach (var column in row.Keys)
            {
                if (!facts.Columns.Any(known =>
                        string.Equals(known.Name, column, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new StepFailure(RunKeys.BadRowColumn, facts.Name + "." + column);
                }
            }

            if (string.Equals(facts.Name, "region", StringComparison.Ordinal)
                && HasTextAndNumbers(row))
            {
                throw new StepFailure(RunKeys.BadRowColumn, facts.Name + ".text");
            }

            foreach (var column in facts.Columns.Where(column => column.Required))
            {
                if (!row.Keys.Any(written =>
                        string.Equals(written, column.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new StepFailure(RunKeys.BadRowColumn, facts.Name + "." + column.Name);
                }
            }
        }
    }

    /// <summary>
    /// A region row says where to look in one of two ways: a name, or four numbers. Both at once
    /// would be two answers to one question, and the reader would have to guess which one wins.
    /// </summary>
    private static bool HasTextAndNumbers(IReadOnlyDictionary<string, ExpressionNode> row)
        => Said(row, "text")
           && new[] { "x", "y", "width", "height" }.Any(column => Said(row, column));

    /// <summary>
    /// True when a value has the shape the catalogue promised for one result. Empty means this
    /// attempt did not work the result out and is valid for every kind. A declared record field
    /// table is exact: a missing, extra or wrongly typed field is a different shape even when the
    /// value still happens to be a record.
    /// </summary>
    public static bool ReturnFits(ActionReturnSemantics declared, Value value)
    {
        if (!ValueRules.Accepts(declared.Type, value))
        {
            return false;
        }

        // An empty answer is how a record or list is handed over when this attempt found nothing.
        // It means "not supplied", not a record with every field missing.
        if (value.IsNull || value.Kind is ValueKind.Text && value.Text.Length == 0)
        {
            return true;
        }

        if (declared.Fields.Count == 0)
        {
            return true;
        }

        var items = declared.Type is ValueKind.List ? value.Items : [value];
        return items.All(item => item.IsNull || RecordFits(declared.Fields, item));
    }

    private static bool RecordFits(IReadOnlyList<ActionReturnField> declared, Value value)
    {
        if (!value.IsRecord || value.Fields.Count != declared.Count)
        {
            return false;
        }

        foreach (var field in declared)
        {
            var found = value.Field(field.Name);
            if (found is null || !ValueRules.Accepts(field.Type, found))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// A condition that asks nothing is a mistake rather than a question answered "no": a branch
    /// whose condition was never filled in would otherwise quietly take one side for ever.
    /// </summary>
    public static void RequireCondition(ExecutableStep step, string name)
    {
        if (string.IsNullOrWhiteSpace(step.Text(name)))
        {
            throw new StepFailure(RunKeys.MissingCondition);
        }
    }

    /// <summary>True when a column of a row holds anything at all.</summary>
    private static bool Said(IReadOnlyDictionary<string, ExpressionNode> row, string column)
        => !Formula.IsNothing(row.GetValueOrDefault(column));
}
