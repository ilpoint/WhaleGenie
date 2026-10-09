using System.Text.Json.Nodes;
using WhaleGenie.Execution;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Tests;

/// <summary>
/// The short name every step goes by. It is what a result variable carries and what a condition
/// points at, so it has to exist while the macro is being written — not only while it runs — and it
/// has to be the same name on the next run as it was on the one before.
/// </summary>
public class StepIdTests
{
    [Fact]
    public void A_step_is_given_a_name_when_it_joins_the_list()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel();
            var step = Step("control.delay");

            editor.AddStep(step);

            Assert.NotEmpty(step.Id);
            Assert.Equal(4, step.Id.Length);
            Assert.All(step.Id, character => Assert.Contains(character, "23456789abcdefghjkmnpqrstvwxyz"));
        });
    }

    [Fact]
    public void No_two_steps_in_a_macro_share_a_name()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel();
            editor.AddStep(Step("control.log"));
            editor.AddStep(Loop(Step("control.delay"), Guard()));
            editor.AddStep(Guard());

            var names = editor.Steps.SelectMany(All).Select(step => step.Id).ToList();

            Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
            Assert.All(names, name => Assert.NotEmpty(name));
        });
    }

    [Fact]
    public void A_name_survives_being_saved_and_read_back()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel();
            var step = Step("control.delay");
            editor.AddStep(step);
            var macro = new MacroItem { Name = "demo" };
            macro.Steps.AddRange(editor.Steps);

            var read = MacroItem.FromJson(macro.ToJson());

            Assert.Equal(step.Id, Assert.Single(read.Steps).Id);
        });
    }

    [Fact]
    public void A_macro_written_before_steps_had_names_gets_them_on_the_way_in()
    {
        Ui.Run(() =>
        {
            // What an older file looks like: two steps, no name on either, and the same name
            // written twice by hand — both settled when the macro is opened.
            var written = JsonNode.Parse(
                """
                {
                  "name": "old",
                  "steps": [
                    { "type": "control.log", "id": "aaaa", "params": {} },
                    { "type": "control.delay", "params": {} },
                    { "type": "control.delay", "id": "aaaa", "params": {} }
                  ]
                }
                """)!.AsObject();

            var read = MacroItem.FromJson(written);
            var editor = new MacroEditorViewModel();
            editor.LoadFrom(read);

            var names = editor.Steps.Select(step => step.Id).ToList();
            Assert.Equal(3, names.Count);
            Assert.All(names, name => Assert.NotEmpty(name));
            Assert.Equal(3, names.Distinct(StringComparer.Ordinal).Count());
            Assert.Contains("aaaa", names);
        });
    }

    [Fact]
    public void Pasting_a_copy_gives_it_a_name_of_its_own()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel();
            var step = Step("control.delay");
            editor.AddStep(step);

            var clipboard = MacroEditorViewModel.SerializeSteps([step]);
            editor.SetSelection([step]);
            editor.PasteSteps(MacroEditorViewModel.DeserializeSteps(clipboard)!);

            Assert.Equal(2, editor.Steps.Count);
            Assert.NotEqual(editor.Steps[0].Id, editor.Steps[1].Id);

            // The step it was copied from keeps the name it had: the copy is the one that moves.
            Assert.Equal(step.Id, editor.Steps[0].Id);
        });
    }

    [Fact]
    public void An_untouched_copy_keeps_its_name_when_it_lands_somewhere_free()
    {
        Ui.Run(() =>
        {
            // Pasting between macros is the same clipboard text, and a name nothing here is using
            // is worth keeping: the conditions in the macro it came from point at it.
            var editor = new MacroEditorViewModel();
            editor.AddStep(Step("control.log"));

            var from = new MacroStep { Type = "control.delay", Id = "k3f9" };
            editor.PasteSteps([from]);

            Assert.Equal("k3f9", editor.Steps[1].Id);
        });
    }

    [Fact]
    public void Editing_a_step_does_not_rename_it()
    {
        Ui.Run(() =>
        {
            var step = Step("control.delay");
            step.Id = "k3f9";
            var dialog = new AddActionViewModel { SelectedDefinition = ActionCatalog.Find("control.delay") };
            dialog.LoadFrom(step);

            var edited = dialog.BuildStep();

            Assert.Equal("k3f9", edited.Id);
        });
    }

    [Fact]
    public void A_run_reports_the_step_under_the_name_in_the_file()
    {
        Ui.Run(() =>
        {
            var written = Step("control.log");
            written.Id = "k3f9";
            var older = Step("control.log");

            var runnable = new[] { written, older }.ToExecutable();

            Assert.Equal("k3f9", runnable[0].Id);

            // A step that has never been saved has no name to run under, so one is made here
            // rather than leaving the engine with two steps it cannot tell apart.
            Assert.NotEmpty(runnable[1].Id);
            Assert.NotEqual(runnable[0].Id, runnable[1].Id);
        });
    }

    [Fact]
    public void The_row_shows_the_name_so_it_can_be_written_down()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel();
            var step = Step("control.log");
            editor.AddStep(step);

            var row = editor.Rows.First(candidate => candidate.IsStep);

            Assert.Equal(step.Id, row.Id);
            Assert.True(row.HasId);
            Assert.Contains(step.Id, row.IdHint, StringComparison.Ordinal);
        });
    }

    private static MacroStep Step(string type, params StepParameter[] parameters)
        => new() { Type = type, Parameters = [.. parameters] };

    /// <summary>A block that holds a step, so the names of nested steps are checked too.</summary>
    private static MacroStep Loop(params MacroStep[] steps)
        => Step("control.repeat", new StepParameter
        {
            Name = "body",
            Kind = ActionParameterKind.Steps,
            Steps = [.. steps],
        });

    /// <summary>A step with a condition inside it, which is a step too and gets a name.</summary>
    private static MacroStep Guard()
        => Step("control.if", new StepParameter
        {
            Name = "condition",
            Kind = ActionParameterKind.Condition,
            Condition = Step("condition.expression"),
        });

    /// <summary>Everything written inside a step, lists and conditions alike.</summary>
    private static IEnumerable<MacroStep> All(MacroStep step)
        => step.Parameters.SelectMany(parameter => parameter.Steps)
            .Concat(step.Parameters
                .Where(parameter => parameter.Condition is not null)
                .Select(parameter => parameter.Condition!))
            .SelectMany(child => new[] { child }.Concat(All(child)));
}
