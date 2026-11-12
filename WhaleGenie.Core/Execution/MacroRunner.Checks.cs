using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Core.Execution;

// Checking a step before it runs: the answers it leaves out and the values it hands back.
public sealed partial class MacroRunner
{
    /// <summary>
    /// Answers one condition. Nothing here waits or hands work to another thread, so a condition
    /// can be asked again and again without holding anything up — which is what the wait block
    /// does with it.
    /// </summary>
    /// <summary>
    /// Whether a condition's answer comes from the screen, and so from a look at it. The ones that
    /// do are the ones whose own "how often the screen is looked at" counts, the same field the
    /// vision steps carry.
    /// </summary>
    private static bool LooksAtScreen(string conditionType)
        => ActionSemanticsCatalog.Find(conditionType)?.LooksAtScreen ?? false;

    /// <summary>
    /// What the catalogue says a step must be given, checked before the step runs. An unanswered
    /// field is a mistake to report rather than an empty value for the action to interpret; a word
    /// outside the choices is refused unless the field says an unknown word falls back. Both use
    /// the key the catalogue names for that field, so a path still says "no path" rather than the
    /// general word for an unanswered field.
    /// </summary>
    private static void ValidateAnswers(ExecutableStep step)
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
            var key = Word(row, FieldNames.Key);
            var name = row.TryGetValue(FieldNames.Name, out var writtenName)
                ? Formula.NameOf(writtenName).Name
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
    /// True when a value has the shape the catalogue promised for one result. Empty means this
    /// attempt did not work the result out and is valid for every kind. A declared record field
    /// table is exact: a missing, extra or wrongly typed field is a different shape even when the
    /// value still happens to be a record.
    /// </summary>
    internal static bool ReturnFits(ActionReturnSemantics declared, Value value)
    {
        if (!MacroValues.Fits(declared.Type, value))
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
            if (found is null || !MacroValues.Fits(field.Type, found))
            {
                return false;
            }
        }

        return true;
    }
    private static void RequireCondition(ExecutableStep step, string name)
    {
        if (string.IsNullOrWhiteSpace(step.Text(name)))
        {
            throw new StepFailure(RunKeys.MissingCondition);
        }
    }
}
