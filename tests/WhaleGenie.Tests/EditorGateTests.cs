using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Editing;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Tests;

/// <summary>
/// The C0 gate: the editor model is usable without a window, a real macro round-trips through it,
/// every catalogue action can build the form the page will draw, and the snapshot/command loop is
/// fast enough to try in the web shell.
/// </summary>
public class EditorGateTests
{
    [Fact]
    public void A_real_macro_opens_edits_saves_and_reopens()
    {
        var workspace = new ProjectWorkspace();
        var macro = new MacroItem { Name = "gate", Description = "before" };
        macro.Steps.Add(new MacroStep { Type = "control.log" });
        workspace.AddMacro(macro);

        var session = HeadlessEditorSession.Open(workspace, "gate");
        Assert.Equal("gate", session.Name);
        Assert.Equal(["control.log"], session.StepTypes);

        session.Patch(description: "after");
        session.AddStep("control.delay");
        Assert.True(session.IsDirty);
        Assert.True(session.Save());

        var saved = Assert.Single(workspace.Macros);
        Assert.Equal("gate", saved.Name);
        Assert.Equal("after", saved.Description);
        Assert.Equal(["control.log", "control.delay"], saved.Steps.Select(step => step.Type));

        var reopened = HeadlessEditorSession.Open(workspace, "gate");
        var snapshot = reopened.Snapshot();
        Assert.Equal("gate", snapshot.Name);
        Assert.Equal("after", snapshot.Description);
        Assert.Equal(2, snapshot.StepCount);
        Assert.Equal(["control.log", "control.delay"], reopened.StepTypes);
    }

    [Fact]
    public void Step_tree_ids_and_parameter_edits_survive_the_session()
    {
        var workspace = new ProjectWorkspace();
        var macro = new MacroItem { Name = "tree" };
        macro.Steps.Add(new MacroStep
        {
            Type = "control.delay",
            Parameters =
            [
                new StepParameter
                {
                    Name = "ms",
                    Kind = ActionParameterKind.Number,
                    Written = WhaleGenie.Core.Expressions.Formula.Number("500", 500),
                },
            ],
        });
        workspace.AddMacro(macro);

        var session = HeadlessEditorSession.Open(workspace, "tree");
        var first = Assert.Single(session.Snapshot().Steps);
        Assert.False(string.IsNullOrEmpty(first.Id));
        Assert.Equal("500", Assert.Single(first.Parameters).Value);

        session.PatchParameter(first.Id, "ms", "750");
        session.AddStep("control.log");
        var steps = session.Snapshot().Steps;
        Assert.Equal("750", Assert.Single(steps[0].Parameters).Value);

        session.MoveStep(steps[1].Id, -1);
        var moved = session.Snapshot().Steps;
        Assert.Equal("control.log", moved[0].Type);
        Assert.Equal("control.delay", moved[1].Type);

        session.RemoveStep(moved[0].Id);
        Assert.Equal("control.delay", Assert.Single(session.Snapshot().Steps).Type);
        Assert.True(session.IsDirty);
    }

    [Fact]
    public void A_step_can_be_added_inside_a_container()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "nested" });
        var session = HeadlessEditorSession.Open(workspace, "nested");

        session.AddStep("control.repeat");
        var parent = Assert.Single(session.Snapshot().Steps);
        Assert.True(parent.AcceptsSteps);

        session.AddStep("control.delay", parent.Id);
        var withChild = Assert.Single(session.Snapshot().Steps);
        Assert.Equal("control.delay", Assert.Single(withChild.Sections.Single().Steps).Type);
    }

    [Fact]
    public void A_global_can_be_added_edited_and_removed()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "globals" });
        var session = HeadlessEditorSession.Open(workspace, "globals");

        session.AddGlobal();
        var added = Assert.Single(session.Snapshot().Globals);
        Assert.Equal("variable", added.Name);

        session.PatchGlobal("variable", "name", "count");
        session.PatchGlobal("count", "type", "number");
        session.PatchGlobal("count", "default", "5");
        session.PatchGlobal("count", "persist", "true");
        var changed = Assert.Single(session.Snapshot().Globals);
        Assert.Equal("count", changed.Name);
        Assert.Equal("number", changed.Type);
        Assert.Equal("5", changed.Default);
        Assert.True(changed.Persisted);

        session.RemoveGlobal("count");
        Assert.Empty(session.Snapshot().Globals);
    }

    [Fact]
    public void Saving_commits_global_variable_draft()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "globals" });
        var session = HeadlessEditorSession.Open(workspace, "globals");

        session.AddGlobal();
        session.PatchGlobal("variable", "name", "count");
        session.PatchGlobal("count", "default", "5");
        session.Save();

        var saved = workspace.Variables.FindGlobal("count");
        Assert.NotNull(saved);
        Assert.Equal("5", saved.DefaultValue);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void Callable_parameters_and_results_can_be_declared()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "callable" });
        var session = HeadlessEditorSession.Open(workspace, "callable");

        session.AddCallableParameter();
        session.PatchCallableParameter(0, "name", "count");
        session.PatchCallableParameter(0, "type", "number");
        session.PatchCallableParameter(0, "required", "true");
        session.AddCallableResult();
        session.PatchCallableResult(0, "name", "total");

        var parameter = Assert.Single(session.CallableParameters);
        Assert.Equal("count", parameter.Name);
        Assert.Equal("number", parameter.Type);
        Assert.True(parameter.Required);
        Assert.Equal("total", Assert.Single(session.CallableResults).Name);

        session.PatchCallableParameter(0, "type", "record");
        session.AddCallableParameterField(0);
        session.PatchCallableParameterField(0, 0, "name", "left");
        session.PatchCallableParameterField(0, 0, "type", "number");
        var field = Assert.Single(Assert.Single(session.CallableParameters).Fields);
        Assert.Equal("left", field.Name);
        Assert.Equal("number", field.Type);

        session.AddCallableParameter();
        session.PatchCallableParameter(1, "name", "second");
        session.MoveCallableParameter(1, -1);
        Assert.Equal("second", session.CallableParameters[0].Name);
    }

    [Fact]
    public void Macro_settings_cover_input_limit_and_toggle_key()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "settings" });
        var session = HeadlessEditorSession.Open(workspace, "settings");

        session.SetMacroSetting("inputLimit", "30");
        session.SetMacroSetting("toggleKey", "F8");
        Assert.False(session.FollowInputLimit);
        Assert.Equal(30, session.InputLimit);
        Assert.Equal("F8", session.ToggleKey);
    }

    [Fact]
    public void A_submacro_can_be_edited_and_saved_into_its_parent()
    {
        var workspace = new ProjectWorkspace();
        var parent = new MacroItem { Name = "parent" };
        parent.SubMacros.Add(new MacroItem { Name = "helper" });
        workspace.AddMacro(parent);
        var session = HeadlessEditorSession.Open(workspace, "parent");
        Assert.Equal(["helper"], session.SubMacros);

        var sub = HeadlessEditorSession.OpenSubmacro(workspace, session, "helper");
        sub.AddStep("control.delay");
        sub.Save();
        session.Save();

        var saved = Assert.Single(workspace.Macros.Single(macro => macro.Name == "parent").SubMacros);
        Assert.Equal("control.delay", Assert.Single(saved.Steps).Type);
    }

    [Fact]
    public void A_step_can_be_duplicated_with_its_settings()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "copies" });
        var session = HeadlessEditorSession.Open(workspace, "copies");
        session.AddStep("control.delay");
        var original = Assert.Single(session.Snapshot().Steps);
        session.SetStepMeta(original.Id, "comment", "copy me");

        session.DuplicateStep(original.Id);
        var steps = session.Snapshot().Steps;
        Assert.Equal(2, steps.Count);
        Assert.Equal("control.delay", steps[1].Type);
        Assert.Equal("copy me", steps[1].Comment);
        Assert.NotEqual(steps[0].Id, steps[1].Id);
    }

    [Fact]
    public void A_step_can_be_disabled_and_enabled()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "steps" });
        var session = HeadlessEditorSession.Open(workspace, "steps");
        session.AddStep("control.delay");

        var step = Assert.Single(session.Snapshot().Steps);
        Assert.True(step.Enabled);
        session.SetStepEnabled(step.Id, false);
        Assert.False(Assert.Single(session.Snapshot().Steps).Enabled);
        session.SetStepEnabled(step.Id, true);
        Assert.True(Assert.Single(session.Snapshot().Steps).Enabled);

        session.SetStepMeta(step.Id, "comment", "retry it");
        session.SetStepMeta(step.Id, "timeoutMs", "500");
        session.SetStepMeta(step.Id, "retryCount", "2");
        session.SetStepMeta(step.Id, "onError", "Continue");
        var changed = Assert.Single(session.Snapshot().Steps);
        Assert.Equal("retry it", changed.Comment);
        Assert.Equal(500, changed.TimeoutMs);
        Assert.Equal(2, changed.RetryCount);
        Assert.Equal("Continue", changed.OnError);
    }

    [Fact]
    public void Recorded_steps_can_be_appended_to_the_active_document()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "recorded" });
        var session = HeadlessEditorSession.Open(workspace, "recorded");
        session.AppendSteps([new MacroStep { Type = "control.delay" }]);

        var step = Assert.Single(session.Snapshot().Steps);
        Assert.Equal("control.delay", step.Type);
        Assert.NotEmpty(step.Id);
    }

    [Fact]
    public void A_condition_parameter_can_edit_rows()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "conditions" });
        var session = HeadlessEditorSession.Open(workspace, "conditions");
        session.AddStep("control.waitUntil");

        var step = Assert.Single(session.Snapshot().Steps);
        var parameter = step.Parameters.Single(item => item.Name == "condition");
        Assert.NotNull(parameter.Condition);
        Assert.True(parameter.Condition!.Editable);

        session.AddConditionGroup(step.Id, "condition");
        session.AddConditionRow(step.Id, "condition", [1]);
        session.SetConditionRow(step.Id, "condition", [1, 0], "left", "$ready");
        session.AddConditionRow(step.Id, "condition");
        session.SetConditionRow(step.Id, "condition", 0, "left", "$found");
        session.SetConditionRow(step.Id, "condition", 0, "compare", "Equal");
        session.SetConditionRow(step.Id, "condition", 0, "right", "1");
        var row = session.Snapshot().Steps.Single().Parameters
            .Single(item => item.Name == "condition").Condition!.Nodes[0];
        Assert.Equal("found", row.Left);
        Assert.Equal("1", row.Right);
        var nested = session.Snapshot().Steps.Single().Parameters
            .Single(item => item.Name == "condition").Condition!.Nodes[1];
        Assert.Equal("group", nested.Kind);
        Assert.Equal("ready", Assert.Single(nested.Children).Left);
    }

    [Fact]
    public void A_row_parameter_can_add_and_fill_rows()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "rows" });
        var session = HeadlessEditorSession.Open(workspace, "rows");
        session.AddStep("control.repeat");

        var step = Assert.Single(session.Snapshot().Steps);
        var returns = step.Parameters.Single(parameter => parameter.Name == "returns");
        Assert.Equal(["key", "name"], returns.Columns.Select(column => column.Name));
        Assert.Empty(returns.Rows);

        session.AddParameterRow(step.Id, "returns");
        session.PatchParameterRow(step.Id, "returns", 0, "key", "found");
        session.PatchParameterRow(step.Id, "returns", 0, "name", "hit");
        var row = Assert.Single(session.Snapshot().Steps.Single().Parameters
            .Single(parameter => parameter.Name == "returns").Rows);
        Assert.Equal(["found", "hit"], row.Cells);
    }

    [Fact]
    public void A_trigger_can_be_added_by_kind()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "kinds" });
        var session = HeadlessEditorSession.Open(workspace, "kinds");

        Assert.Contains(HeadlessEditorSession.TriggerKindOptions, kind => kind.Value == "Timer");
        session.AddTrigger("Timer");
        var trigger = Assert.Single(session.Snapshot().Triggers);
        Assert.Equal("Timer", trigger.Kind);

        session.SetTriggerTimer(trigger.Id, "interval", "7");
        session.SetTriggerTimer(trigger.Id, "unit", "Minutes");
        var timer = Assert.Single(session.Snapshot().Triggers);
        Assert.Equal("7", timer.Interval);
        Assert.Equal("Minutes", timer.Unit);

        session.AddTrigger("File");
        var file = session.Snapshot().Triggers.Single(item => item.Kind == "File");
        session.SetTriggerFile(file.Id, "path", @"C:\notes");
        session.SetTriggerFile(file.Id, "change", "Changed");
        session.SetTriggerFile(file.Id, "filter", "*.txt");
        session.SetTriggerFile(file.Id, "subfolders", "true");
        var watched = session.Snapshot().Triggers.Single(item => item.Kind == "File");
        Assert.Equal(@"C:\notes", watched.FilePath);
        Assert.Equal("Changed", watched.FileChange);
        Assert.Equal("*.txt", watched.FileFilter);
        Assert.True(watched.FileSubfolders);

        session.AddTrigger("Process");
        var process = session.Snapshot().Triggers.Single(item => item.Kind == "Process");
        session.SetTriggerProcess(process.Id, "name", "notepad");
        session.SetTriggerProcess(process.Id, "change", "Stopped");
        var watchedProcess = session.Snapshot().Triggers.Single(item => item.Kind == "Process");
        Assert.Equal("notepad", watchedProcess.ProcessName);
        Assert.Equal("Stopped", watchedProcess.ProcessChange);

        session.AddTrigger("Hotstring");
        var hotstring = session.Snapshot().Triggers.Single(item => item.Kind == "Hotstring");
        session.SetTriggerHotstring(hotstring.Id, "hello");
        Assert.Equal("hello",
            session.Snapshot().Triggers.Single(item => item.Kind == "Hotstring").HotstringText);

        session.AddTrigger("Color");
        var colour = session.Snapshot().Triggers.Single(item => item.Kind == "Color");
        session.SetTriggerColor(colour.Id, "hex", "#FF0000");
        session.SetTriggerColor(colour.Id, "tolerance", "7");
        session.SetTriggerColor(colour.Id, "follow", "true");
        var watchedColour = session.Snapshot().Triggers.Single(item => item.Kind == "Color");
        Assert.Equal("#FF0000", watchedColour.ColorHex);
        Assert.Equal("7", watchedColour.ColorTolerance);
        Assert.True(watchedColour.FollowCursor);

        session.AddTrigger("Idle");
        var idle = session.Snapshot().Triggers.Single(item => item.Kind == "Idle");
        session.SetTriggerIdle(idle.Id, "90");
        Assert.Equal("90", session.Snapshot().Triggers.Single(item => item.Kind == "Idle").IdleSeconds);

        session.AddTrigger("Window");
        var window = session.Snapshot().Triggers.Single(item => item.Kind == "Window");
        session.SetTriggerWindow(window.Id, "title", "Notepad");
        session.SetTriggerWindow(window.Id, "how", "TitleEquals");
        session.SetTriggerWindow(window.Id, "change", "Disappeared");
        var watchedWindow = session.Snapshot().Triggers.Single(item => item.Kind == "Window");
        Assert.Equal("Notepad", watchedWindow.WindowTitle);
        Assert.Equal("TitleEquals", watchedWindow.WindowHow);
        Assert.Equal("Disappeared", watchedWindow.WindowChange);

        session.AddTrigger("Interface");
        var face = session.Snapshot().Triggers.Single(item => item.Kind == "Interface");
        session.SetTriggerInterface(face.Id, "title", "Game");
        session.SetTriggerInterface(face.Id, "how", "TitleContains");
        var watchedFace = session.Snapshot().Triggers.Single(item => item.Kind == "Interface");
        Assert.Equal("Game", watchedFace.InterfaceTitle);
        Assert.Equal("TitleContains", watchedFace.InterfaceHow);
    }

    [Fact]
    public void A_macro_group_can_be_changed()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "grouped" });
        var session = HeadlessEditorSession.Open(workspace, "grouped");

        session.Patch(group: "Tools");
        Assert.Equal("Tools", session.Snapshot().Group);
    }

    [Fact]
    public void A_step_can_be_undone_and_redone()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "history" });
        var session = HeadlessEditorSession.Open(workspace, "history");

        session.AddStep("control.delay");
        Assert.Single(session.Snapshot().Steps);
        Assert.True(session.CanUndo);

        session.Undo();
        Assert.Empty(session.Snapshot().Steps);
        Assert.True(session.CanRedo);

        session.Redo();
        Assert.Single(session.Snapshot().Steps);
    }

    [Fact]
    public void A_local_variable_can_be_added_renamed_typed_and_removed()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "variables" });
        var session = HeadlessEditorSession.Open(workspace, "variables");

        session.AddVariable();
        Assert.Single(session.Snapshot().Variables);

        session.PatchVariable(string.Empty, "name", "count");
        session.PatchVariable("count", "initial", "5");
        session.PatchVariable("count", "type", "number");
        var variable = Assert.Single(session.Snapshot().Variables);
        Assert.Equal("count", variable.Name);
        Assert.Contains(session.ValueChoices, choice => choice.Name == "count");
        Assert.Equal("number", variable.Type);
        Assert.Equal("5", variable.Initial);
        Assert.True(variable.IsDeclared);

        session.RemoveVariable("count");
        Assert.Empty(session.Snapshot().Variables);
        Assert.True(session.IsDirty);
    }

    [Fact]
    public void A_dirty_document_cannot_be_closed_until_saved()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "closable" });
        var documents = new HeadlessEditorWorkspace(workspace);
        var session = documents.Open("closable");

        session.Patch(description: "changed");
        Assert.Throws<InvalidOperationException>(() => documents.CloseActive());
        Assert.Single(documents.Tabs);

        Assert.True(session.Save());
        documents.CloseActive();
        Assert.Empty(documents.Tabs);
    }

    [Fact]
    public void Saving_an_invalid_macro_is_refused()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "valid" });
        var session = HeadlessEditorSession.Open(workspace, "valid");

        session.Patch(name: string.Empty);
        Assert.False(session.CanSave);
        Assert.NotEmpty(session.SaveProblem);
        Assert.Throws<InvalidOperationException>(() => session.Save());

        session.Patch(name: "still-valid");
        Assert.True(session.CanSave);
        Assert.True(session.Save());
    }

    [Fact]
    public void A_blank_document_can_be_added_without_a_project_macro()
    {
        var workspace = new ProjectWorkspace();
        var documents = new HeadlessEditorWorkspace(workspace);

        var first = documents.New();
        Assert.Same(first, documents.Active);
        Assert.Single(documents.Tabs);

        documents.New();
        Assert.Equal(2, documents.Tabs.Count);
        Assert.Empty(workspace.Macros);
    }

    [Fact]
    public void Two_documents_keep_their_own_edits_when_switched()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "one" });
        workspace.AddMacro(new MacroItem { Name = "two" });
        var documents = new HeadlessEditorWorkspace(workspace);

        documents.Open("one").Patch(description: "edited one");
        documents.Open("two").Patch(description: "edited two");
        Assert.Equal(2, documents.Tabs.Count);

        var one = documents.Activate("one");
        Assert.Equal("edited one", one.Description);
        Assert.True(one.IsDirty);

        var two = documents.Activate("two");
        Assert.Equal("edited two", two.Description);
        Assert.True(two.IsDirty);
    }

    [Fact]
    public void A_key_trigger_can_be_added_bound_and_removed()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "triggered" });

        var session = HeadlessEditorSession.Open(workspace, "triggered");
        Assert.Empty(session.Triggers);

        session.AddKeyTrigger();
        var trigger = Assert.Single(session.Triggers);
        Assert.Equal("Key", trigger.Kind);

        session.SetTriggerBinding(trigger.Id, "F8");
        session.SetTriggerLoop(trigger.Id, "Hold");
        session.SetTriggerOnce(trigger.Id, true);
        session.SetTriggerEnabled(trigger.Id, false);
        var changed = Assert.Single(session.Snapshot().Triggers);
        Assert.Equal("F8", changed.Binding);
        Assert.Equal("Hold", changed.Loop);
        Assert.True(changed.Once);
        Assert.False(changed.Enabled);

        session.RemoveTrigger(trigger.Id);
        Assert.Empty(session.Snapshot().Triggers);
        Assert.True(session.IsDirty);
    }

    [Fact]
    public void Every_catalogue_action_builds_a_form()
    {
        Ui.Run(() =>
        {
            var actions = ActionCatalog.Definitions;
            Assert.True(actions.Count >= 177, $"only {actions.Count} actions are in the catalogue");

            foreach (var action in actions)
            {
                var form = new AddActionViewModel(actions)
                {
                    SelectedDefinition = action,
                };

                Assert.NotNull(form.SelectedDefinition);
                Assert.Equal(action.Key, form.SelectedDefinition!.Key);
            }
        });
    }

    [Fact]
    public void Snapshot_carries_sections_details_and_problems()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "tree" });
        var session = HeadlessEditorSession.Open(workspace, "tree");

        var branch = session.AddStep("control.if");
        session.AddStep("control.log", branch.Id, "then");
        session.AddStep("control.stop", branch.Id, "else");
        var delay = session.AddStep("control.delay");
        session.PatchParameter(delay.Id, "ms", "500");
        var waiting = session.AddStep("control.waitUntil");

        var snapshot = session.Snapshot();
        var block = snapshot.Steps.Single(step => step.Id == branch.Id);
        Assert.Equal(new[] { "then", "else" }, block.Sections.Select(section => section.Name));
        Assert.All(block.Sections, section =>
            Assert.False(string.IsNullOrWhiteSpace(section.Label)));
        Assert.Equal("control.log", Assert.Single(block.Sections[0].Steps).Type);
        Assert.Equal("control.stop", Assert.Single(block.Sections[1].Steps).Type);

        var delayed = snapshot.Steps.Single(step => step.Id == delay.Id);
        Assert.Contains("500", delayed.Detail);
        Assert.False(string.IsNullOrWhiteSpace(delayed.Id));

        var incomplete = snapshot.Steps.Single(step => step.Id == waiting.Id);
        Assert.False(string.IsNullOrWhiteSpace(incomplete.Problem));
    }

    [Fact]
    public void Snapshot_carries_action_categories_and_groups()
    {
        var categories = HeadlessEditorSession.ActionCategories();
        Assert.Null(categories[0].Value);
        Assert.Equal("Input", categories[1].Value);
        Assert.DoesNotContain(categories, category => category.Value == "Control");

        var actions = HeadlessEditorSession.Actions();
        var delay = actions.Single(action => action.Key == "control.delay");
        Assert.Equal("Control", delay.Category);
        Assert.Equal("Waiting", delay.Group);
        Assert.False(delay.GroupLabel.StartsWith("Palette.", StringComparison.Ordinal));
        Assert.False(string.IsNullOrWhiteSpace(delay.CategoryShort));

        var pressed = actions.Single(action => action.Key == "input.keyPress");
        Assert.Equal("Input", pressed.Category);
        Assert.Equal("Keyboard", pressed.Group);
        Assert.Equal(0, pressed.GroupRank);
        Assert.Contains(actions, action => action.Category == "Vision");
    }

    [Fact]
    public void Snapshot_carries_variable_groups_and_persistence()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "variables" });
        var session = HeadlessEditorSession.Open(workspace, "variables");

        session.AddGlobal();
        session.PatchGlobal("variable", "name", "shared");
        session.PatchGlobal("shared", "persist", "true");
        session.AddVariable();
        session.PatchVariable("", "name", "local");
        Assert.True(session.Save());

        var choices = session.Snapshot().ValueChoices;
        var shared = choices.Single(choice => choice.Name == "shared");
        Assert.Equal("Global", shared.Group);
        Assert.True(shared.Persistent);
        Assert.False(string.IsNullOrWhiteSpace(shared.Display));

        var local = choices.Single(choice => choice.Name == "local");
        Assert.Equal("Local", local.Group);
        Assert.Contains(choices, choice => choice.Group == "System");
    }

    [Fact]
    public void Tabs_keep_dirty_state_and_submacro_documents()
    {
        var workspace = new ProjectWorkspace();
        var parent = new MacroItem { Name = "one" };
        parent.SubMacros.Add(new MacroItem { Name = "sub" });
        workspace.AddMacro(parent);
        workspace.AddMacro(new MacroItem { Name = "two" });

        var documents = new HeadlessEditorWorkspace(workspace);
        var first = documents.Open("one");
        documents.Open("two");
        first.Patch(description: "edited");

        var tabs = documents.Tabs;
        Assert.True(tabs.Single(tab => tab.Name == "one").Dirty);
        Assert.False(tabs.Single(tab => tab.Name == "two").Dirty);

        var sub = documents.OpenSubmacro("one", "sub");
        Assert.True(documents.Tabs.Single(tab => tab.Name == "sub").IsSubMacro);
        Assert.Same(sub, documents.Active);
    }

    [Fact]
    public void A_step_can_be_added_to_a_named_list_with_presets()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "preset" });
        var session = HeadlessEditorSession.Open(workspace, "preset");

        var attempt = session.AddStep("control.try");
        session.AddStep("control.delay", attempt.Id, "catch");
        var call = session.AddStep("control.runMacro", null, null,
            new Dictionary<string, string> { ["macro"] = "helper" });

        var snapshot = session.Snapshot();
        var block = snapshot.Steps.Single(step => step.Id == attempt.Id);
        Assert.Equal("control.delay",
            Assert.Single(block.Sections.Single(section => section.Name == "catch").Steps).Type);
        Assert.Empty(block.Sections.Single(section => section.Name == "body").Steps);

        var step = snapshot.Steps.Single(candidate => candidate.Id == call.Id);
        Assert.Equal("helper",
            Assert.Single(step.Parameters, parameter => parameter.Name == "macro").Value);
    }

    [Fact]
    public void A_hundred_snapshot_and_patch_round_trips_stay_quick()
    {
        var workspace = new ProjectWorkspace();
        var macro = new MacroItem { Name = "speed" };
        macro.Steps.Add(new MacroStep { Type = "control.log" });
        workspace.AddMacro(macro);

        var session = HeadlessEditorSession.Open(workspace, "speed");
        var watch = Stopwatch.StartNew();
        for (var index = 0; index < 100; index++)
        {
            session.Patch(description: "round " + index);
            _ = session.Snapshot();
        }

        watch.Stop();

        Assert.Equal("round 99", session.Description);
        Assert.True(watch.ElapsedMilliseconds < 5000,
            $"100 snapshot/patch rounds took {watch.ElapsedMilliseconds} ms");
    }
}
