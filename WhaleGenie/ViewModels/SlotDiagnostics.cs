using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;


namespace WhaleGenie.ViewModels;

// Marking the piece a failure belongs to, and the sentence it carries.
/// <summary>
/// Points a complaint at the piece that caused it. The engine says what went wrong but not where a
/// character stands, so the band matches the offending name against its own pieces and checks the
/// arguments against the same function table the language uses. Nothing here writes the formula.
/// </summary>
internal static class SlotDiagnostics
{
    public static void Mark(SlotNode? recipe, ExpressionException? error,
        IReadOnlyDictionary<string, ExpressionFunction> table, IReadOnlySet<string> known)
    {
        if (recipe is null)
        {
            return;
        }

        foreach (var piece in recipe.Walk())
        {
            piece.HasProblem = false;
            piece.ProblemHint = string.Empty;
        }

        if (error is not null)
        {
            var described = ExpressionText.Describe(error);
            if (error.Code is ExpressionErrorCode.UnknownFunction or ExpressionErrorCode.ArgumentCount)
            {
                var calls = recipe.Walk().OfType<SlotCall>().ToList();
                var named = calls.FirstOrDefault(call =>
                    error.Detail.Equals(call.Name, StringComparison.OrdinalIgnoreCase)
                    || error.Detail.StartsWith(call.Name + "(", StringComparison.OrdinalIgnoreCase));
                Problem(named ?? calls.LastOrDefault() ?? (SlotNode)recipe, described);
            }
            else
            {
                Problem(recipe, described);
            }
        }

        foreach (var call in recipe.Walk().OfType<SlotCall>())
        {
            if (!table.TryGetValue(call.Name, out var entry))
            {
                continue;
            }

            if (call.Arguments.Count < entry.MinArguments)
            {
                var place = call.Ghost
                    ?? call.Arguments.LastOrDefault()?.Items.OfType<SlotEmpty>().FirstOrDefault();
                if (place is not null)
                {
                    Problem(place, Strings.Get("ValuePicker.Problem.MissingArgument"));
                }
            }

            if (entry.MaxArguments < call.Arguments.Count)
            {
                Problem(call.Arguments[entry.MaxArguments],
                    Strings.Get("ValuePicker.Problem.ExtraArgument"));
            }
        }
        foreach (var value in recipe.Walk().OfType<SlotValue>())
        {
            if (!value.IsName)
            {
                continue;
            }

            var name = value.Text.TrimStart('$');
            if (name.Length == 0 || known.Contains(name))
            {
                continue;
            }

            // A field of a record is read off the name before the dot; the record itself is what
            // has to be declared, not the field.
            var dot = name.IndexOf('.');
            if (dot > 0 && known.Contains(name[..dot]))
            {
                continue;
            }

            Problem(value, Strings.Get("ValuePicker.Problem.Undeclared"));
        }
    }

    private static void Problem(SlotNode piece, string hint)
    {
        piece.HasProblem = true;
        piece.ProblemHint = hint;
    }
}
