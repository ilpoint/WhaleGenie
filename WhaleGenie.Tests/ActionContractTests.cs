using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.Tests;

/// <summary>
/// The editor is a projection of the engine's action catalogue. These checks fail when the
/// display side grows a second copy of an engine fact, or when one of the two sides loses an
/// action, parameter or result the other still has.
/// </summary>
public class ActionContractTests
{
    [Fact]
    public void Every_displayed_action_projects_one_engine_definition()
    {
        var problems = new List<string>();

        Ui.Run(() =>
        {
            foreach (var display in ActionCatalog.Definitions)
            {
                var engine = ActionSemanticsCatalog.Find(display.Key);
                if (engine is null)
                {
                    problems.Add($"{display.Key}: no engine definition");
                    continue;
                }

                SameValue(problems, display.Key, "category", display.Category, engine.Category);
                SameValue(problems, display.Key, "hidden", display.Hidden, engine.Hidden);
                SameValue(problems, display.Key, "input", display.SendsInput, engine.SendsInput);
                SameValue(problems, display.Key, "condition", display.IsCondition, engine.IsCondition);
                SameValue(problems, display.Key, "screen", display.LooksAtScreen, engine.LooksAtScreen);
                SameValue(problems, display.Key, "waits", display.Waits, engine.Waits);
                SameValue(problems, display.Key, "timeout source", display.TimeoutSource,
                    engine.TimeoutSource);
                SameValue(problems, display.Key, "timeout field", display.TimeoutField,
                    engine.TimeoutField);
                SameValue(problems, display.Key, "step timeout", display.StepTimeoutMs,
                    engine.StepTimeoutMs);

                var displayNames = display.Parameters.Select(parameter => parameter.Name).ToList();
                var engineNames = engine.Parameters.Select(parameter => parameter.Name).ToList();
                if (!displayNames.SequenceEqual(engineNames, StringComparer.Ordinal))
                {
                    problems.Add($"{display.Key}: parameters are "
                        + $"[{string.Join(", ", displayNames)}] in the editor and "
                        + $"[{string.Join(", ", engineNames)}] in the engine");
                }

                foreach (var parameter in display.Parameters)
                {
                    if (engine.Parameter(parameter.Name) is not { } facts)
                    {
                        continue;
                    }

                    var field = $"{display.Key}.{parameter.Name}";
                    SameValue(problems, field, "shape", ShapeOf(parameter.Kind), facts.Shape);
                    SameValue(problems, field, "required", parameter.Required, facts.Required);
                    SameValue(problems, field, "default", parameter.DefaultValue, facts.DefaultValue);
                    SameValue(problems, field, "variables", parameter.AcceptsVariables,
                        facts.AcceptsVariables);
                    SameValue(problems, field, "conditions only", parameter.ConditionsOnly,
                        facts.ConditionsOnly);
                    Same(problems, field, "options", parameter.Options, facts.Options);
                    Same(problems, field, "child keys", parameter.ChildKeys, facts.ChildKeys);

                    if (parameter.NamesVariable != (facts.ReadMode == ActionReadMode.VariableName))
                    {
                        problems.Add($"{field}: the editor and the engine disagree about writing a name");
                    }

                    if (parameter.AcceptsFormula
                        != (facts.ReadMode == ActionReadMode.WholeValue))
                    {
                        problems.Add($"{field}: the editor's formula fact differs from the engine read mode");
                    }

                    var picker = facts.Picker;
                    if (picker == ActionPickerKind.Window
                        && parameter.Kind != ActionParameterKind.Window)
                    {
                        problems.Add($"{field}: a window picker sits on a field that is not a window");
                    }

                    if (parameter.Kind == ActionParameterKind.Window
                        && picker != ActionPickerKind.Window)
                    {
                        problems.Add($"{field}: a window field has no window picker");
                    }

                    if (parameter.Kind is ActionParameterKind.Image or ActionParameterKind.Images
                        && picker != ActionPickerKind.Image)
                    {
                        problems.Add($"{field}: a picture field has no picture picker");
                    }

                    if (parameter.Kind == ActionParameterKind.Color
                        && picker != ActionPickerKind.Color)
                    {
                        problems.Add($"{field}: a colour field has no colour picker");
                    }

                    if (picker == ActionPickerKind.Element
                        && parameter.Name is not ("uiSelector" or "anchorSelector"))
                    {
                        problems.Add($"{field}: an element picker sits on a field that names no element");
                    }

                    if (picker == ActionPickerKind.Browser
                        && (parameter.Name != "pageSelector"
                            || !display.Key.StartsWith("browser.", StringComparison.Ordinal)))
                    {
                        problems.Add($"{field}: a browser picker sits on the wrong field");
                    }

                    if (picker == ActionPickerKind.Region
                        && parameter.Kind is not ActionParameterKind.Region
                        && parameter.Name is not ("x" or "startX"))
                    {
                        problems.Add($"{field}: a region picker sits on a field that names no rectangle");
                    }
                }

                var displayReturns = display.Returns.Select(result => $"{result.Key}:{result.Type}")
                    .ToList();
                var engineReturns = engine.Returns.Select(result => $"{result.Key}:{result.Type}")
                    .ToList();
                if (!displayReturns.SequenceEqual(engineReturns, StringComparer.Ordinal))
                {
                    problems.Add($"{display.Key}: returns are "
                        + $"[{string.Join(", ", displayReturns)}] in the editor and "
                        + $"[{string.Join(", ", engineReturns)}] in the engine");
                }
            }
        });

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void The_two_catalogues_hold_the_same_actions()
    {
        var displayed = Ui.Run(() => ActionCatalog.Definitions.Select(action => action.Key).ToList());
        var engine = ActionSemanticsCatalog.All.Select(action => action.Key).ToList();

        Assert.Equal(engine, displayed);
        Assert.Contains("control.case", engine);
        Assert.True(ActionSemanticsCatalog.Find("control.case")!.Hidden);
    }

    /// <summary>
    /// A field that says it follows another field is only a question while that other field is on
    /// the same action. A rename that leaves the follower pointing at the old name makes the field
    /// disappear without any error, so every reference and every value it names is checked here.
    /// </summary>
    [Fact]
    public void A_displayed_field_only_follows_a_real_sibling()
    {
        var problems = new List<string>();

        Ui.Run(() =>
        {
            foreach (var action in ActionCatalog.Definitions)
            {
                foreach (var parameter in action.Parameters)
                {
                    Check(action, parameter, parameter.Name, "AppliesWhen",
                        parameter.AppliesWhen, parameter.AppliesWhenValues, requireValues: true);
                    Check(action, parameter, parameter.Name, "AppliesWhenAlso",
                        parameter.AppliesWhenAlso, parameter.AppliesWhenAlsoValues, requireValues: true);
                    Check(action, parameter, parameter.Name, "EnabledBySibling",
                        parameter.EnabledBySibling, [], requireValues: false);
                }
            }
        });

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));

        void Check(ActionDefinition action, ActionParameter parameter, string field,
            string fact, string siblingName, IReadOnlyList<string> values, bool requireValues)
        {
            if (siblingName.Length == 0)
            {
                if (values.Count > 0)
                {
                    problems.Add($"{action.Key}.{field}: {fact}Values is set without a sibling");
                }

                return;
            }

            var sibling = action.Parameters.FirstOrDefault(candidate =>
                candidate.Name == siblingName);
            if (sibling is null)
            {
                problems.Add($"{action.Key}.{field}: {fact} names {siblingName}, which the action has not");
                return;
            }

            if (requireValues && values.Count == 0)
            {
                problems.Add($"{action.Key}.{field}: {fact} names {siblingName} but no values");
                return;
            }

            if (!requireValues)
            {
                return;
            }

            foreach (var value in values.Where(value => value != ActionParameter.AnyValue))
            {
                var valid = sibling.Shape switch
                {
                    _ when value.Length == 0 => true,
                    ActionValueShape.Choice => sibling.Options.Contains(value, StringComparer.Ordinal),
                    ActionValueShape.Flag => value is "true" or "false",
                    // A number or text field can be compared with any literal; the sibling only
                    // has to exist for the question to be asked.
                    _ => true,
                };
                if (!valid)
                {
                    problems.Add($"{action.Key}.{field}: {fact} expects {siblingName}={value}, "
                        + "which that field cannot be");
                }
            }
        }
    }

    [Fact]
    public void The_display_catalogue_holds_no_engine_facts()
    {
        var display = File.ReadAllText(Path.Combine(Repository(), "WhaleGenie", "Models",
            "ActionDisplay.json"));
        string[] engineFacts =
        [
            "AcceptsFormula", "Category", "DefaultValue", "Hidden", "NamesVariable", "Options", "Picker",
            "ReadMode", "Required", "SendsInput", "Shape", "StepTimeoutMs", "Waits",
            "ConditionsOnly", "ChildKeys", "AcceptsVariables", "TimeoutSource", "TimeoutField",
            "Unit", "EmptyMeans", "Minimum", "Maximum", "EmptiesToEdge", "IsDuration",
        ];

        using var document = JsonDocument.Parse(display);
        var seen = new List<string>();
        Keys(document.RootElement, seen);

        var duplicated = seen.Where(key => engineFacts.Contains(key, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.True(duplicated.Count == 0,
            "the display catalogue declares engine facts again: " + string.Join(", ", duplicated));

        var source = File.ReadAllText(Path.Combine(Repository(), "WhaleGenie", "Models",
            "ActionCatalog.cs"));
        Assert.DoesNotContain("Key = \"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SendsInput =", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StepTimeoutMs =", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ActionParameterSemantics", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_action_has_the_facts_its_contract_needs()
    {
        var problems = new List<string>();

        foreach (var action in ActionSemanticsCatalog.All)
        {
            if (action.Category == ActionCategory.Condition && !action.IsCondition)
            {
                problems.Add($"{action.Key}: a condition is not marked as one");
            }

            if (action.Waits && action.TimeoutSource == ActionTimeoutSource.None)
            {
                problems.Add($"{action.Key}: waits without saying where its timeout comes from");
            }

            if (action.StepTimeoutMs > 0 && !action.Waits)
            {
                problems.Add($"{action.Key}: has a step timeout but is not marked as waiting");
            }

            foreach (var parameter in action.Parameters)
            {
                if (parameter.Shape == ActionValueShape.Choice && parameter.Options.Count == 0)
                {
                    problems.Add($"{action.Key}.{parameter.Name}: a choice has no options");
                }

                if (parameter.Picker == ActionPickerKind.Browser
                    && !action.Key.StartsWith("browser.", StringComparison.Ordinal))
                {
                    problems.Add($"{action.Key}.{parameter.Name}: a browser picker outside a browser action");
                }
            }

            if (action.Returns.Any(result => string.IsNullOrWhiteSpace(result.Key)))
            {
                problems.Add($"{action.Key}: a return has no key");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    /// <summary>
    /// Every number says what it counts and what a blank cell comes to, and the unit is what puts
    /// the box's limits there: a coordinate can be negative, a length of time starts at zero, and
    /// no pixel field borrows the longest pause as its ceiling. A field that its action refuses
    /// beyond a narrower limit says that limit itself.
    /// </summary>
    [Fact]
    public void Every_number_says_what_it_counts_and_what_an_empty_cell_means()
    {
        var problems = new List<string>();

        foreach (var action in ActionSemanticsCatalog.All)
        {
            foreach (var parameter in action.Parameters)
            {
                var field = $"{action.Key}.{parameter.Name}";
                if (parameter.Shape != ActionValueShape.Number)
                {
                    if (parameter.Unit is not null || parameter.EmptyMeans is not null)
                    {
                        problems.Add($"{field}: a field that is not a number declares a number fact");
                    }

                    continue;
                }

                if (parameter.Unit is null)
                {
                    problems.Add($"{field}: no unit");
                }

                if (parameter.EmptyMeans is null)
                {
                    problems.Add($"{field}: no empty meaning");
                }

                if (parameter.MinimumValue > parameter.MaximumValue)
                {
                    problems.Add($"{field}: its range is empty");
                }

                switch (parameter.Unit)
                {
                    case ActionUnit.Pixels:
                        if (parameter.MaximumValue == StepMeta.LongestPauseMs)
                        {
                            problems.Add($"{field}: a pixel field stops at the longest pause");
                        }

                        break;
                    case ActionUnit.Milliseconds:
                        if (parameter.MinimumValue < 0m)
                        {
                            problems.Add($"{field}: a length of time starts below zero");
                        }

                        break;
                    case ActionUnit.Percent:
                        if (parameter.MinimumValue < 0m || parameter.MaximumValue > 100m)
                        {
                            problems.Add($"{field}: a share is outside 0 to 100");
                        }

                        break;
                    case ActionUnit.Axis:
                        if (parameter.MinimumValue != -100m || parameter.MaximumValue != 100m)
                        {
                            problems.Add($"{field}: a stick is outside -100 to 100");
                        }

                        break;
                }

                if (parameter.Name is "x" or "y" or "startX" or "startY" or "endX" or "endY"
                    && parameter.MinimumValue >= 0m)
                {
                    problems.Add($"{field}: a place cannot be negative");
                }

                if (parameter.Name is "width" or "height" && parameter.MinimumValue < 1m)
                {
                    problems.Add($"{field}: a size may be less than one pixel");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    /// <summary>
    /// A place or a rectangle is written one way: the record field that takes it whole, or the
    /// numbers beside it. Every action that works at a place says so with a Point and every one
    /// that works on a rectangle says Rect, and no action still carries the switch that used to
    /// choose between the two.
    /// </summary>
    [Fact]
    public void A_place_or_a_rectangle_is_declared_as_one_shape()
    {
        var retired = new[] { "useRecord", "record", "startFromRecord", "startRecord",
            "endFromRecord", "endRecord" };
        foreach (var action in ActionSemanticsCatalog.All)
        {
            foreach (var name in retired)
            {
                Assert.Null(action.Parameter(name));
            }
        }

        (string Key, string Field, ActionValueShape Shape)[] places =
        [
            ("input.mouseMove", "place", ActionValueShape.Point),
            ("input.mouseClick", "place", ActionValueShape.Point),
            ("input.mouseDoubleClick", "place", ActionValueShape.Point),
            ("input.mouseDown", "place", ActionValueShape.Point),
            ("input.mouseUp", "place", ActionValueShape.Point),
            ("input.mouseScroll", "place", ActionValueShape.Point),
            ("vision.getPixel", "place", ActionValueShape.Point),
            ("vision.waitColor", "place", ActionValueShape.Point),
            ("input.mouseDrag", "startPlace", ActionValueShape.Point),
            ("input.mouseDrag", "endPlace", ActionValueShape.Point),
            ("vision.capture", "rect", ActionValueShape.Rect),
            ("ocr.recognize", "rect", ActionValueShape.Rect),
            ("window.move", "rect", ActionValueShape.Rect),
        ];

        foreach (var (key, name, shape) in places)
        {
            var field = ActionSemanticsCatalog.Find(key)!.Parameter(name);
            Assert.NotNull(field);
            Assert.Equal(shape, field.Shape);
            Assert.Equal(ActionReadMode.WholeValue, field.ReadMode);
            Assert.False(field.Required);
        }

        // The same question is not asked twice: no place or rectangle action also carries a
        // second field of the other shape.
        foreach (var action in ActionSemanticsCatalog.All)
        {
            var shapes = action.Parameters.Select(parameter => parameter.Shape).ToList();
            Assert.False(shapes.Contains(ActionValueShape.Point) && shapes.Contains(ActionValueShape.Rect));
        }
    }

    /// <summary>
    /// One beat goes by one name. A field that says the same thing under a second name is a second
    /// question for the person filling the form in, and a second answer for the engine to read.
    /// </summary>
    [Fact]
    public void No_action_carries_a_second_name_for_the_same_beat()
    {
        foreach (var action in ActionSemanticsCatalog.All)
        {
            Assert.Null(action.Parameter("pollMs"));
        }
    }

    /// <summary>
    /// A record's fields are the catalogue's, not the engine's private shape: every record a step
    /// hands back says what it contains, every field name is one of the constants the file format
    /// and the record readers share, and only a record (or a list of them) carries fields at all.
    /// </summary>
    [Fact]
    public void Every_record_says_what_its_fields_are()
    {
        var constants = typeof(FieldNames)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var action in ActionSemanticsCatalog.All)
        {
            foreach (var result in action.Returns)
            {
                var field = $"{action.Key}.{result.Key}";
                if (result.Type == ActionReturnType.Record)
                {
                    Assert.NotEmpty(result.Fields);
                }
                else if (result.Type != ActionReturnType.List)
                {
                    Assert.Empty(result.Fields);
                }

                Assert.All(result.Fields, declared =>
                {
                    Assert.Contains(declared.Name, constants);
                    Assert.NotEqual(ActionReturnType.Image, declared.Type);
                });
            }
        }
    }

    /// <summary>
    /// The manual's record table is what a user reads a field name off, so it has to name exactly
    /// the fields the catalogue declares — no field the engine writes is missing, and no field the
    /// manual promises is gone.
    /// </summary>
    [Fact]
    public void The_manual_lists_exactly_the_record_fields_the_catalogue_declares()
    {
        var declared = ActionSemanticsCatalog.All
            .SelectMany(action => action.Returns)
            .SelectMany(result => result.Fields)
            .Select(field => field.Name)
            .ToHashSet(StringComparer.Ordinal);

        var manual = File.ReadAllLines(Path.Combine(Repository(), "doc", "manual.md"));
        var documented = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in manual.Where(line =>
                     line.StartsWith("| 命中（", StringComparison.Ordinal)
                     || line.StartsWith("| 截图（", StringComparison.Ordinal)
                     || line.StartsWith("| 窗口（", StringComparison.Ordinal)
                     || line.StartsWith("| 显示器（", StringComparison.Ordinal)
                     || line.StartsWith("| 程序（", StringComparison.Ordinal)
                     || line.StartsWith("| 元素（", StringComparison.Ordinal)))
        {
            var cells = line.Split('|');
            if (cells.Length < 3)
            {
                continue;
            }

            foreach (Match match in Regex.Matches(cells[2], "`([^`]+)`"))
            {
                documented.Add(match.Groups[1].Value);
            }
        }

        Assert.NotEmpty(documented);
        Assert.Equal(declared.Order(StringComparer.Ordinal),
            documented.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// carrying a second timeout field would start a fresh clock on each retry, which is exactly
    /// what the user cannot see from the step's own settings.
    /// </summary>
    [Fact]
    public void No_action_carries_a_second_timeout_field()
    {
        foreach (var action in ActionSemanticsCatalog.All)
        {
            Assert.Null(action.Parameter("timeoutMs"));
            Assert.NotEqual(ActionTimeoutSource.Parameter, action.TimeoutSource);
        }

        // Looking for an element is a question about now, not a wait; waiting for one has its own
        // action and its own step timeout.
        var exists = ActionSemanticsCatalog.Find("uia.exists");
        Assert.NotNull(exists);
        Assert.False(exists.Waits);
        Assert.Equal(ActionTimeoutSource.None, exists.TimeoutSource);
    }

    /// <summary>
    /// The two things a user has to know before choosing a timeout: every try spends the same
    /// budget, and retrying a block runs its completed steps over again.
    /// </summary>
    [Fact]
    public void The_timeout_and_block_hints_say_what_a_retry_costs()
    {
        Assert.Contains("共用", Strings.Chinese["Add.StepTimeoutHint"], StringComparison.Ordinal);
        Assert.Contains("整块重来", Strings.Chinese["Add.BlockSettings"], StringComparison.Ordinal);

        var manual = File.ReadAllText(Path.Combine(Repository(), "doc", "manual.md"));
        Assert.Contains("共用", manual, StringComparison.Ordinal);
        Assert.Contains("整块重来", manual, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_input_action_is_declared_and_no_reader_is_declared_as_one()
    {
        string[] sending =
        [
            "clipboard.copy", "clipboard.paste",
            "ocr.clickText",
            "uia.click",
            "vision.clickImage", "vision.clickDetect",
        ];

        foreach (var key in sending)
        {
            Assert.True(ActionSemanticsCatalog.Find(key)!.SendsInput, key);
            Assert.True(MacroRunner.SendsInput(key), key);
        }

        foreach (var key in new[]
                 {
                     "input.keyPress", "input.mouseClick", "gamepad.button",
                     "vision.findImage", "browser.click", "uia.invoke",
                 })
        {
            Assert.Equal(key.StartsWith("input.", StringComparison.Ordinal)
                         || key.StartsWith("gamepad.", StringComparison.Ordinal),
                ActionSemanticsCatalog.Find(key)!.SendsInput);
        }

        Assert.False(MacroRunner.SendsInput("vision.findImage"));
        Assert.False(MacroRunner.SendsInput("not.an.action"));
    }

    [Fact]
    public void The_runner_dispatches_every_executable_action_and_asks_every_condition()
    {
        var source = File.ReadAllText(Path.Combine(Repository(), "WhaleGenie.Core", "Execution",
            "MacroRunner.cs"));
        var dispatch = Cases(Method(source, "Dispatch"));
        var conditions = Cases(Method(source, "Check"));

        var executable = ActionSemanticsCatalog.All
            .Where(action => !action.IsCondition && action.Key != "control.case")
            .Select(action => action.Key)
            .Order(StringComparer.Ordinal)
            .ToList();
        var declared = ActionSemanticsCatalog.Conditions
            .Select(action => action.Key)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(executable, dispatch.Order(StringComparer.Ordinal));
        Assert.Equal(declared, conditions.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_executable_action_is_named_by_at_least_one_engine_test()
    {
        var tests = string.Join(Environment.NewLine,
            Directory.EnumerateFiles(Path.Combine(Repository(), "WhaleGenie.Core.Tests"),
                    "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText));

        var missing = ActionSemanticsCatalog.All
            .Where(action => !action.IsCondition && action.Key != "control.case")
            .Where(action => !tests.Contains($"\"{action.Key}\"", StringComparison.Ordinal))
            .Select(action => action.Key)
            .ToList();

        Assert.True(missing.Count == 0,
            "these actions have no engine test that names them: " + string.Join(", ", missing));
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
        ActionParameterKind.Bool => ActionValueShape.Flag,
        ActionParameterKind.Choice => ActionValueShape.Choice,
        ActionParameterKind.Steps => ActionValueShape.Steps,
        ActionParameterKind.Condition => ActionValueShape.Condition,
        ActionParameterKind.Region or ActionParameterKind.KeySequence
            or ActionParameterKind.Images or ActionParameterKind.Returns
            or ActionParameterKind.Narrow => ActionValueShape.Rows,
        ActionParameterKind.Image => ActionValueShape.Image,
        ActionParameterKind.Record => ActionValueShape.Record,
        ActionParameterKind.Point => ActionValueShape.Point,
        ActionParameterKind.Rect => ActionValueShape.Rect,
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
