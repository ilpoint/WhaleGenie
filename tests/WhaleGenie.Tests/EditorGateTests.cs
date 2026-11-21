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
        Assert.Equal("control.delay", Assert.Single(withChild.Steps).Type);
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
