using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Editing;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.Tests;

/// <summary>
/// The rules a callable child is made and removed by, without a session around them: the name it
/// gets when one is taken, the copy that owns its own steps, the call that reaches it, and the call
/// that keeps it from being removed.
/// </summary>
public class SubMacroEditorTests
{
    [Fact]
    public void A_taken_name_grows_a_number_after_the_stem()
    {
        Assert.Equal("helper", SubMacroEditor.UniqueName(["other"], "helper"));
        Assert.Equal("helper 2", SubMacroEditor.UniqueName(["helper"], "helper"));
        Assert.Equal("Helper 3", SubMacroEditor.UniqueName(["helper", "helper 2"], "Helper"));
        Assert.Equal("helper 2", SubMacroEditor.UniqueName(["HELPER"], "helper"));
    }

    [Fact]
    public void An_unnamed_child_is_given_a_free_default()
    {
        // The two tables rather than the current language, so a case beside this one changing the
        // interface language cannot make the name the host picks look wrong.
        var defaults = new[]
        {
            Strings.English["Editor.NewSubMacroName"],
            Strings.Chinese["Editor.NewSubMacroName"],
        };
        var unnamed = SubMacroEditor.NewSubMacro([], null);
        Assert.Contains(unnamed.Name, defaults);

        var taken = new HashSet<string>(defaults, StringComparer.OrdinalIgnoreCase);
        var child = SubMacroEditor.NewSubMacro(taken, null);

        Assert.DoesNotContain(child.Name, taken);
        Assert.NotEqual(unnamed.Name, child.Name);
        Assert.Empty(child.Steps);
        Assert.Empty(child.SubMacros);
    }

    [Fact]
    public void A_copy_owns_its_steps_and_takes_a_free_name()
    {
        var source = new MacroItem { Name = "helper", Description = "before" };
        source.Parameters.Add(new MacroParameter { Name = "count", Type = ValueKind.Number });
        source.Steps.Add(new MacroStep
        {
            Type = "control.delay",
            Parameters =
            [
                new StepParameter
                {
                    Name = "ms",
                    Kind = ActionParameterKind.Number,
                    Written = Formula.Number("500", 500),
                },
            ],
        });

        var copy = SubMacroEditor.Copy(source, ["helper"], null);

        Assert.Equal("helper 2", copy.Name);
        Assert.Equal("before", copy.Description);
        Assert.Equal("count", Assert.Single(copy.Parameters).Name);
        Assert.NotSame(source.Steps[0], Assert.Single(copy.Steps));

        copy.Steps[0].Parameters[0].Written = Formula.Number("900", 900);
        copy.Steps.Add(new MacroStep { Type = "control.log" });

        Assert.Equal("500", source.Steps[0].Parameters[0].Value);
        Assert.Single(source.Steps);
    }

    [Fact]
    public void A_call_step_asks_for_what_the_child_declares()
    {
        var child = new MacroItem { Name = "helper" };
        child.Parameters.Add(new MacroParameter { Name = "count", Type = ValueKind.Number });
        child.Results.Add(new MacroResult { Name = "total", Type = ValueKind.Number });

        var call = SubMacroEditor.CallStep(child);

        Assert.Equal("control.runMacro", call.Type);
        Assert.Equal("helper",
            Assert.Single(call.Parameters, parameter => parameter.Name == "macro").Value);
        Assert.Single(Assert.Single(call.Parameters,
            parameter => parameter.Name == FieldNames.Arguments).Rows);
        Assert.Single(Assert.Single(call.Parameters,
            parameter => parameter.Name == "returns").Rows);
    }

    [Fact]
    public void A_call_anywhere_in_the_steps_references_the_child()
    {
        var owner = new MacroItem { Name = "root" };
        owner.SubMacros.Add(new MacroItem { Name = "helper" });
        Assert.False(SubMacroEditor.Referenced(owner, "helper"));

        var call = new MacroStep
        {
            Type = "control.runMacro",
            Parameters =
            [
                new StepParameter
                {
                    Name = "macro",
                    Kind = ActionParameterKind.Macro,
                    Written = Formula.Text("\"HELPER\""),
                },
            ],
        };
        owner.Steps.Add(new MacroStep
        {
            Type = "control.repeat",
            Parameters =
            [
                new StepParameter
                {
                    Name = "times",
                    Kind = ActionParameterKind.Number,
                    Written = Formula.Number("2", 2),
                },
                new StepParameter { Name = "body", Kind = ActionParameterKind.Steps, Steps = [call] },
            ],
        });

        Assert.True(SubMacroEditor.Referenced(owner, "helper"));
        Assert.True(SubMacroEditor.Referenced(owner, " HELPER "));
        Assert.False(SubMacroEditor.Referenced(owner, "other"));
    }

    [Fact]
    public void A_child_still_called_is_kept_and_a_free_one_is_removed()
    {
        var owner = new MacroItem { Name = "root" };
        owner.SubMacros.Add(new MacroItem { Name = "helper" });
        owner.Steps.Add(SubMacroEditor.CallStep(owner.SubMacros[0]));

        Assert.False(SubMacroEditor.Delete(owner, "helper"));
        Assert.Single(owner.SubMacros);

        owner.Steps.Clear();

        Assert.True(SubMacroEditor.Delete(owner, "HELPER"));
        Assert.Empty(owner.SubMacros);
        Assert.False(SubMacroEditor.Delete(owner, "helper"));
    }
}
