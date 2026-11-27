using System;
using System.IO;
using System.Linq;
using WhaleGenie.Contracts;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Editing;
using WhaleGenie.Models;
using Xunit;

namespace WhaleGenie.Tests;

public class ValueEditorTests
{
    [Fact]
    public void A_number_field_opens_in_value_mode_and_confirms_a_shape()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "values" });
        var session = HeadlessEditorSession.Open(workspace, "values");
        var step = session.AddStep("control.delay");

        var snapshot = session.OpenValue(step.Id, "ms");
        Assert.Equal("Value", snapshot.Mode);
        Assert.True(snapshot.ShowsBand);
        Assert.NotNull(snapshot.Recipe);
        Assert.True(snapshot.CanConfirm);

        session.ValueCommand(new ValueEditorCommand("clear"));
        session.ValueCommand(new ValueEditorCommand("place", Token: "750"));
        var outcome = session.ConfirmValue();

        Assert.True(outcome.Closed);
        Assert.True(outcome.Applied);
        var stored = step.Parameters.Single(parameter => parameter.Name == "ms");
        Assert.Equal("750", stored.Value);
    }

    [Fact]
    public void A_text_field_opens_in_text_mode_and_writes_the_body()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "text" });
        var session = HeadlessEditorSession.Open(workspace, "text");
        var step = session.AddStep("control.note");

        var snapshot = session.OpenValue(step.Id, "note");
        Assert.Equal("Text", snapshot.Mode);
        Assert.False(snapshot.ShowsBand);

        session.OpenValue(step.Id, "note");
        session.ValueCommand(new ValueEditorCommand("textBody", Text: "hello"));
        // Text mode edits the body directly; the old picker uses TextBody, which is the same
        // input as a text field and is confirmed as a text shape.
        var outcome = session.ConfirmValue();

        Assert.True(outcome.Closed);
        Assert.True(outcome.Applied);
        Assert.Equal("hello", step.Parameters.Single(parameter => parameter.Name == "note").Value);
    }

    [Fact]
    public void Token_commands_build_and_confirm_an_expression()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "tokens" });
        var session = HeadlessEditorSession.Open(workspace, "tokens");
        var step = session.AddStep("control.delay");

        session.OpenValue(step.Id, "ms");
        session.ValueCommand(new ValueEditorCommand("clear"));
        session.ValueCommand(new ValueEditorCommand("place", Token: "1"));
        session.ValueCommand(new ValueEditorCommand("place", Token: " + "));
        session.ValueCommand(new ValueEditorCommand("place", Token: "2"));

        var beforeWrap = session.ValueState();
        Assert.True(beforeWrap.ShowsBand);
        Assert.NotNull(beforeWrap.Recipe);
        Assert.Contains("1", beforeWrap.Text);
        Assert.Contains("2", beforeWrap.Text);

        session.ValueCommand(new ValueEditorCommand("wrapGroup"));
        var outcome = session.ConfirmValue();
        Assert.True(outcome.Closed);
        Assert.True(outcome.Applied);
        var stored = step.Parameters.Single(parameter => parameter.Name == "ms");
        Assert.Contains("1", stored.Value);
        Assert.Contains("2", stored.Value);
    }



    [Fact]
    public void A_local_variable_initial_keeps_a_name_sent_as_parts()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "locals" });
        var session = HeadlessEditorSession.Open(workspace, "locals");
        session.AddVariable();
        session.PatchVariable(string.Empty, "name", "slot");
        session.PatchVariable("slot", "initial", string.Empty,
            [new EditorValuePartSnapshot("name", string.Empty, "count")]);

        var variable = session.Snapshot().Variables.Single(entry => entry.IsDeclared);
        Assert.Equal("count", variable.Initial);
        Assert.Contains(variable.InitialParts,
            part => part.Kind == "name" && part.Name == "count");
    }


    [Fact]
    public void A_local_variable_initial_opens_in_value_mode_and_confirms()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "locals-value" });
        var session = HeadlessEditorSession.Open(workspace, "locals-value");
        session.AddVariable();
        session.PatchVariable(string.Empty, "name", "slot");

        var snapshot = session.OpenVariableValue("local", "slot");
        Assert.Equal("Value", snapshot.Mode);
        session.ValueCommand(new ValueEditorCommand("clear"));
        session.ValueCommand(new ValueEditorCommand("place", Token: "5"));
        var outcome = session.ConfirmValue();

        Assert.True(outcome.Applied);
        Assert.Equal("5", session.Snapshot().Variables.Single(entry => entry.IsDeclared).Initial);
    }

    [Fact]
    public void A_global_variable_default_keeps_a_name_sent_as_parts()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "globals" });
        var session = HeadlessEditorSession.Open(workspace, "globals");
        session.AddGlobal();
        session.PatchGlobal("variable", "default", string.Empty,
            [new EditorValuePartSnapshot("name", string.Empty, "count")]);

        var global = session.Snapshot().Globals.Single(entry => entry.Name == "variable");
        Assert.Equal("count", global.Default);
        Assert.Contains(global.DefaultParts,
            part => part.Kind == "name" && part.Name == "count");
    }

    [Fact]
    public void A_declaration_default_opens_in_value_mode_and_confirms()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "declaration" });
        var session = HeadlessEditorSession.Open(workspace, "declaration");
        session.AddCallableParameter();
        session.PatchCallableParameter(0, "name", "amount");

        var snapshot = session.OpenDeclarationValue(0);
        Assert.Equal("Value", snapshot.Mode);
        Assert.True(snapshot.ShowsBand);

        session.ValueCommand(new ValueEditorCommand("clear"));
        session.ValueCommand(new ValueEditorCommand("place", Token: "42"));
        var outcome = session.ConfirmValue();

        Assert.True(outcome.Closed);
        Assert.True(outcome.Applied);
        Assert.Equal("42", session.Snapshot().CallableParameters[0].Default);
    }

    [Fact]
    public void A_declaration_default_keeps_a_name_sent_as_parts()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "declaration-parts" });
        var session = HeadlessEditorSession.Open(workspace, "declaration-parts");
        session.AddCallableParameter();
        session.PatchCallableParameter(0, "name", "amount");

        session.PatchCallableParameter(0, "default", string.Empty,
            [new EditorValuePartSnapshot("name", string.Empty, "count")]);

        var parameter = session.Snapshot().CallableParameters[0];
        Assert.Equal("count", parameter.Default);
        Assert.Contains(parameter.DefaultParts, part => part.Kind == "name" && part.Name == "count");
    }

    [Fact]
    public void A_name_field_can_take_a_variable_and_confirm_it()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "names" });
        var session = HeadlessEditorSession.Open(workspace, "names");
        var step = session.AddStep("control.setVariable");
        session.AddVariable();
        session.PatchVariable("", "name", "chosen");

        var snapshot = session.OpenValue(step.Id, "name");
        Assert.Equal("Name", snapshot.Mode);
        Assert.True(snapshot.IsNameMode);

        session.ValueCommand(new ValueEditorCommand("variable", Name: "chosen"));
        var outcome = session.ConfirmValue();
        Assert.True(outcome.Closed);
        Assert.True(outcome.Applied);
        Assert.Equal("chosen",
            step.Parameters.Single(parameter => parameter.Name == "name").Value);
    }

    [Fact]
    public void Empty_text_clears_the_field()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "empty" });
        var session = HeadlessEditorSession.Open(workspace, "empty");
        var step = session.AddStep("control.note");

        session.OpenValue(step.Id, "note");
        session.ValueCommand(new ValueEditorCommand("textBody", Text: ""));
        var outcome = session.ConfirmValue();

        Assert.True(outcome.Closed);
        Assert.True(outcome.Applied);
        Assert.True(string.IsNullOrEmpty(
            step.Parameters.Single(parameter => parameter.Name == "note").Value));
    }

    private static HeadlessEditorSession Open(out ProjectWorkspace workspace)
    {
        workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "values" });
        return HeadlessEditorSession.Open(workspace, "values");
    }

    [Fact]
    public void A_condition_cell_opens_in_the_token_window_and_writes_back()
    {
        var session = Open(out _);
        var step = session.AddStep("control.waitUntil");
        session.AddConditionRow(step.Id, "condition");

        var snapshot = session.OpenConditionValue(step.Id, "condition", [0], "left");
        Assert.True(snapshot.ShowsBand);
        session.ValueCommand(new ValueEditorCommand("clear"));
        session.ValueCommand(new ValueEditorCommand("place", Token: "found"));
        var outcome = session.ConfirmValue();

        Assert.True(outcome.Applied);
        Assert.Contains("found", step.Parameters.Single(parameter => parameter.Name == "condition").Value);
    }

    [Fact]
    public void A_row_table_cell_opens_in_the_token_window_and_writes_back()
    {
        var session = Open(out _);
        var step = session.AddStep("control.repeat");
        session.AddParameterRow(step.Id, "returns");

        var snapshot = session.OpenRowValue(step.Id, "returns", 0, "key");
        Assert.True(snapshot.ShowsBand);
        session.ValueCommand(new ValueEditorCommand("clear"));
        session.ValueCommand(new ValueEditorCommand("place", Token: "found"));
        var outcome = session.ConfirmValue();

        Assert.True(outcome.Applied);
        var row = step.Parameters.Single(parameter => parameter.Name == "returns").Rows[0];
        Assert.Contains(row.Cells.Values, value => Expression.Render(value).Contains("found"));
    }

    [Fact]
    public void Cancelling_a_value_does_not_change_the_step()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "cancel" });
        var session = HeadlessEditorSession.Open(workspace, "cancel");
        var step = session.AddStep("control.delay");
        var before = step.Parameters.Single(parameter => parameter.Name == "ms").Value;

        session.OpenValue(step.Id, "ms");
        session.ValueCommand(new ValueEditorCommand("place", Token: "999"));
        var outcome = session.CancelValue();

        Assert.True(outcome.Closed);
        Assert.False(outcome.Applied);
        Assert.Equal(before, step.Parameters.Single(parameter => parameter.Name == "ms").Value);
    }
}
