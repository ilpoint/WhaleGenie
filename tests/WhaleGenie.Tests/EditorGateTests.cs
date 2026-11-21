using System.Diagnostics;
using System.Linq;
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
