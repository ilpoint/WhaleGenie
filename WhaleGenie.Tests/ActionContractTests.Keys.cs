using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;


namespace WhaleGenie.Tests;

// Names and keys: every executable action named, every missing key real, and the columns a rows field declares.
public partial class ActionContractTests
{
    [Fact]
    public void Every_executable_action_is_named_by_at_least_one_engine_test()
    {
        // Both suites name actions: the engine's own, and the ones that reach a real Windows
        // adapter through the engine. Either counts as naming the action.
        var tests = string.Join(Environment.NewLine,
            new[] { "WhaleGenie.Core.Tests", "WhaleGenie.Platform.Tests" }
                .SelectMany(suite => Directory.EnumerateFiles(
                    Path.Combine(Repository(), suite), "*.cs", SearchOption.AllDirectories))
                .Select(File.ReadAllText));

        var missing = ActionSemanticsCatalog.All
            .Where(action => action.Key != "control.case")
            .Where(action => !tests.Contains($"\"{action.Key}\"", StringComparison.Ordinal))
            .Select(action => action.Key)
            .ToList();

        Assert.True(missing.Count == 0,
            "these actions have no engine test that names them: " + string.Join(", ", missing));
    }

    /// <summary>
    /// Every parameter says which failure key its own absence and its own wrong word report. A key
    /// that is not one of the run's failure keys would reach the user as a name they cannot branch
    /// on, so the catalogue and the vocabulary are held together here.
    /// </summary>
    [Fact]
    public void Every_parameter_names_a_real_key_for_its_missing_and_choice_answers()
    {
        var known = RunKeys.All.ToHashSet(StringComparer.Ordinal);

        foreach (var action in ActionSemanticsCatalog.All)
        {
            foreach (var parameter in action.Parameters)
            {
                Assert.Contains(parameter.MissingKey, known);
                Assert.Contains(parameter.OptionKey, known);
                if (parameter.Conditional)
                {
                    Assert.True(parameter.Required, action.Key + "." + parameter.Name);
                }

                if (parameter.AllowsUnknownOption)
                {
                    Assert.Equal(ActionValueShape.Choice, parameter.Shape);
                }
            }
        }
    }

    /// <summary>
    /// Every row list says which columns a row may hold. The shapes listed here are the ones the
    /// editor writes today; a list that grows a column without the catalogue saying so would pass
    /// the design-time check and be dropped at the run.
    /// </summary>
    [Fact]
    public void Every_rows_field_declares_the_columns_its_rows_may_hold()
    {
        var schemas = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["returns"] = ["key", "name"],
            ["arguments"] = ["name", "value", "given"],
            ["region"] = ["text", "x", "y", "width", "height"],
            ["narrow"] = ["how", "value"],
            ["images"] = ["image"],
            ["keySequence"] = ["keys", "holdMs", "gapMs"],
            ["points"] = ["dx", "dy", "colour", "mustNot", "tolerance"],
        };

        foreach (var action in ActionSemanticsCatalog.All)
        {
            foreach (var parameter in action.Parameters
                         .Where(parameter => parameter.Shape is ActionValueShape.Rows))
            {
                Assert.True(schemas.ContainsKey(parameter.Name),
                    action.Key + "." + parameter.Name + " has no schema");
                Assert.Equal(schemas[parameter.Name],
                    parameter.Columns.Select(column => column.Name));
                Assert.Equal(parameter.Columns.Count,
                    parameter.Columns.Select(column => column.Name)
                        .Distinct(StringComparer.OrdinalIgnoreCase).Count());
            }
        }
    }

    [Fact]
    public void The_runner_no_longer_guesses_facts_from_action_prefixes()
    {
        var source = File.ReadAllText(Path.Combine(Repository(), "WhaleGenie.Core", "Execution",
            "MacroRunner.cs"));

        string[] guesses =
        [
            "StartsWith(\"condition.", "StartsWith(\"input.", "StartsWith(\"gamepad.",
            "StartsWith(\"vision.", "StartsWith(\"ocr.",
        ];

        Assert.All(guesses, guess => Assert.DoesNotContain(guess, source, StringComparison.Ordinal));
    }

    [Fact]
    public void The_engine_catalogue_does_not_use_the_interface_toolkit()
    {
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Repository(), "WhaleGenie.Core"),
                     "*.cs", SearchOption.AllDirectories))
        {
            Assert.DoesNotContain("Avalonia", File.ReadAllText(file), StringComparison.Ordinal);
        }
    }

    private static ActionValueShape ShapeOf(ActionParameterKind kind) => kind switch
    {
        ActionParameterKind.Number => ActionValueShape.Number,
        ActionParameterKind.Bool => ActionValueShape.Bool,
        ActionParameterKind.Choice => ActionValueShape.Choice,
        ActionParameterKind.Steps => ActionValueShape.Steps,
        ActionParameterKind.Region or ActionParameterKind.KeySequence
            or ActionParameterKind.Images or ActionParameterKind.Returns
            or ActionParameterKind.Arguments or ActionParameterKind.Narrow
            or ActionParameterKind.ColourPoints
            => ActionValueShape.Rows,
        ActionParameterKind.Image => ActionValueShape.Image,
        ActionParameterKind.Record => ActionValueShape.Record,
        ActionParameterKind.Point => ActionValueShape.Point,
        ActionParameterKind.Rect => ActionValueShape.Rect,
        ActionParameterKind.WindowRule => ActionValueShape.WindowRule,
        ActionParameterKind.Code => ActionValueShape.Text,
        _ => ActionValueShape.Text,
    };

    private static void SameValue<T>(List<string> problems, string field, string fact, T left, T right)
    {
        if (!EqualityComparer<T>.Default.Equals(left, right))
        {
            problems.Add($"{field}: {fact} is {left} in the editor and {right} in the engine");
        }
    }

    private static void Same(List<string> problems, string field, string fact,
        IEnumerable<string> left, IEnumerable<string> right)
    {
        if (!left.SequenceEqual(right, StringComparer.Ordinal))
        {
            problems.Add($"{field}: {fact} is [{string.Join(", ", left)}] in the editor and "
                + $"[{string.Join(", ", right)}] in the engine");
        }
    }

    /// <summary>
    /// The case labels under each family's own dispatch. Every runner file may carry one, and an
    /// action is answered by whichever family file it belongs to rather than by one list in the
    /// main file.
    /// </summary>
    private static IEnumerable<string> FamilyDispatches(string source)
    {
        foreach (Match declaration in Regex.Matches(source,
                     @"\n    private [^\n]*\bDispatch[A-Za-z]*\("))
        {
            var start = declaration.Index;
            var end = source.IndexOf("\n    private ", start + 1, StringComparison.Ordinal);
            var body = end < 0 ? source[start..] : source[start..end];
            foreach (var key in Cases(body))
            {
                yield return key;
            }
        }
    }
    private static List<string> Cases(string method)
        => [.. Regex.Matches(method, "case \"([^\"]+)\":")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)];

    private static void Keys(JsonElement element, List<string> keys)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    keys.Add(property.Name);
                    Keys(property.Value, keys);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Keys(item, keys);
                }

                break;
        }
    }

    private static string Method(string source, string name)
    {
        var declaration = Regex.Match(source,
            $@"\n    private [^\n]*\b{Regex.Escape(name)}\(");
        Assert.True(declaration.Success, $"{name} was not found");
        var start = declaration.Index;
        var end = source.IndexOf("\n    private ", start + 1, StringComparison.Ordinal);
        return end < 0 ? source[start..] : source[start..end];
    }

    private static string Repository()
    {
        for (var at = new DirectoryInfo(AppContext.BaseDirectory); at is not null; at = at.Parent)
        {
            if (File.Exists(Path.Combine(at.FullName, "WhaleGenie.slnx")))
            {
                return at.FullName;
            }
        }

        throw new InvalidOperationException("WhaleGenie.slnx was not found above the test binaries.");
    }
}
