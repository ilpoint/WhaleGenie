using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;


namespace WhaleGenie.Tests;

// The shape a field has: one place for coordinates and window rules, and one engine definition behind each shown action.
public partial class ActionContractTests
{
    [Fact]
    public void Coordinates_and_window_rules_have_one_shape()
    {
        foreach (var action in ActionSemanticsCatalog.All)
        {
            Assert.DoesNotContain(action.Parameters, parameter =>
                parameter.Name is "anchorWindow" or "targetWindow" or "anchorHow" or "targetHow"
                    or "anchorSelector");

            if (action.Parameter("anchorMode") is { } mode)
            {
                Assert.Equal(["screen", "window"], mode.Options);
                Assert.Equal("anchorMode", action.Parameters[0].Name);
                Assert.Equal("window", action.Parameters[1].Name);
            }

            foreach (var window in action.Parameters.Where(parameter =>
                         parameter.Shape is ActionValueShape.WindowRule))
            {
                Assert.Equal(ActionPickerKind.Window, window.Picker);
                Assert.Equal("window", window.Name);
            }

            if (action.Key.StartsWith("input.", StringComparison.Ordinal)
                && action.Key != "input.waitKey")
            {
                Assert.Equal("inputMode", action.Parameters[0].Name);
            }

        }
    }

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
                SameValue(problems, display.Key, "screen", display.LooksAtScreen, engine.LooksAtScreen);
                SameValue(problems, display.Key, "waits", display.Waits, engine.Waits);
                SameValue(problems, display.Key, "signature source", display.SignatureSource,
                    engine.SignatureSource);
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
                        && parameter.Kind is not ActionParameterKind.WindowRule)
                    {
                        problems.Add($"{field}: a window picker sits on a field that is not a window");
                    }

                    if (parameter.Kind is ActionParameterKind.WindowRule
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

                    // 一个选择器只说一个元素，所以带元素拾取器的格子只有这两格：找哪个元素，
                    // 以及它要挨着的那个元素（锚点）。
                    if (picker == ActionPickerKind.Element
                        && parameter.Name is not ("uiSelector" or "anchorElement"))
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

                    // 「等兄弟填了才可填」等的是一个**步骤列表**（条件组里那条逻辑词要两条条件才成立），
                    // 于是这一格构造时就是关的，只有列表长到两条才被打开。把它指向一个文本框，
                    // 这一格就永远停在灰的状态——没有人会去打开它，也没有人会报错。
                    if (parameter.EnabledBySibling.Length > 0
                        && engine.Parameter(parameter.EnabledBySibling) is not { Shape: ActionValueShape.Steps })
                    {
                        problems.Add($"{field}: waits on {parameter.EnabledBySibling}, "
                            + "which is not a list of steps");
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
}
