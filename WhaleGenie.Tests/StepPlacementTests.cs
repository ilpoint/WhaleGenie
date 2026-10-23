using System.Linq;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Tests;

public class StepPlacementTests
{
    [Fact]
    public void A_case_can_be_pasted_inside_a_case_list()
    {
        Ui.Run(() =>
        {
            var first = Case("one");
            var choice = Switch(first);
            var editor = Editor(choice);
            var cases = choice.StepLists.Single(parameter => parameter.Name == "cases").Steps;

            editor.SetSelection([first]);
            editor.PasteSteps([Case("two")]);

            Assert.Equal(["control.case", "control.case"], cases.Select(step => step.Type));
            Assert.False(editor.HasPlacementNotice);
        });
    }

    [Fact]
    public void An_ordinary_step_cannot_be_pasted_into_a_case_list()
    {
        Ui.Run(() =>
        {
            var first = Case("one");
            var choice = Switch(first);
            var editor = Editor(choice);
            var cases = choice.StepLists.Single(parameter => parameter.Name == "cases").Steps;

            editor.SetSelection([first]);
            editor.PasteSteps([Step("control.log")]);

            Assert.Same(first, Assert.Single(cases));
            Assert.Equal(Strings.Format("Editor.PlaceTakesOnly",
                ActionCatalog.Find("control.case")!.LocalName), editor.PlacementNotice);

            editor.UndoCommand.Execute(null);
            Assert.Empty(editor.Steps);
        });
    }

    [Fact]
    public void A_case_cannot_be_pasted_at_the_top_of_a_macro()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel();

            editor.PasteSteps([Case("one")]);

            Assert.Empty(editor.Steps);
            Assert.Equal(Strings.Get("Editor.PlaceCaseOnly"), editor.PlacementNotice);
        });
    }

    [Fact]
    public void Direct_add_append_and_replace_all_refuse_a_case_outside_a_switch()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel();
            var ordinary = Step("control.log");
            editor.AddStep(ordinary);

            editor.ReplaceStep(ordinary, Case("replacement"));
            Assert.Same(ordinary, Assert.Single(editor.Steps));
            Assert.Equal(Strings.Get("Editor.PlaceCaseOnly"), editor.PlacementNotice);

            editor.AppendSteps([Case("appended")]);
            Assert.Same(ordinary, Assert.Single(editor.Steps));
            Assert.Equal(Strings.Get("Editor.PlaceCaseOnly"), editor.PlacementNotice);

            var empty = new MacroEditorViewModel();
            empty.AddStep(Case("added"));
            Assert.Empty(empty.Steps);
            Assert.Equal(Strings.Get("Editor.PlaceCaseOnly"), empty.PlacementNotice);
        });
    }

    [Fact]
    public void A_loaded_invalid_tree_is_flagged_without_blocking_the_draft()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            macro.Steps.Add(Case("one"));
            var editor = new MacroEditorViewModel();

            editor.LoadFrom(macro);

            Assert.Equal("control.case", Assert.Single(editor.Steps).Type);
            Assert.True(editor.HasStepProblems);
            Assert.True(editor.CanSave);
        });
    }

    [Fact]
    public void Moving_an_ordinary_step_into_a_case_list_is_refused()
    {
        Ui.Run(() =>
        {
            var first = Case("one");
            var choice = Switch(first);
            var editor = Editor(choice);
            var ordinary = Step("control.log");
            editor.AddStep(ordinary);

            editor.SetSelection([ordinary]);
            editor.MoveSelectionTo(
                editor.Rows.Single(row => row.IsHead && row.List?.Name == "cases").RowIndex);

            Assert.Equal(["control.switch", "control.log"],
                editor.Steps.Select(step => step.Type));
            Assert.Same(first, Assert.Single(
                choice.StepLists.Single(parameter => parameter.Name == "cases").Steps));
            Assert.True(editor.HasPlacementNotice);
        });
    }

    [Fact]
    public void Moving_a_case_out_of_its_switch_is_refused()
    {
        Ui.Run(() =>
        {
            var first = Case("one");
            var choice = Switch(first);
            var editor = Editor(choice);

            editor.SetSelection([first]);
            editor.MoveSelectionTo(editor.Rows.Count);

            Assert.Same(choice, Assert.Single(editor.Steps));
            Assert.Same(first, Assert.Single(
                choice.StepLists.Single(parameter => parameter.Name == "cases").Steps));
            Assert.Equal(Strings.Get("Editor.PlaceCaseOnly"), editor.PlacementNotice);
        });
    }

    [Fact]
    public void A_nested_case_outside_a_case_list_rejects_the_whole_paste()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel();
            var group = Step("control.sequence",
                Body("steps", Case("one")));

            editor.PasteSteps([group]);

            Assert.Empty(editor.Steps);
            Assert.Equal(Strings.Get("Editor.PlaceCaseOnly"), editor.PlacementNotice);
        });
    }

    [Fact]
    public void A_case_cannot_be_grouped_or_lifted_out_of_its_list()
    {
        Ui.Run(() =>
        {
            var first = Case("one");
            var choice = Switch(first);
            var editor = Editor(choice);
            MacroItem? lifted = null;
            editor.MacroLifted += macro => lifted = macro;

            editor.SetSelection([first]);
            editor.GroupSelectedCommand.Execute(null);
            Assert.Same(first, Assert.Single(
                choice.StepLists.Single(parameter => parameter.Name == "cases").Steps));
            Assert.True(editor.HasPlacementNotice);

            editor.SetSelection([first]);
            editor.LiftSelectedCommand.Execute(null);
            Assert.Null(lifted);
            Assert.Same(first, Assert.Single(
                choice.StepLists.Single(parameter => parameter.Name == "cases").Steps));
            Assert.True(editor.HasPlacementNotice);
        });
    }

    [Fact]
    public void An_ordinary_step_inside_a_case_body_can_still_be_grouped()
    {
        Ui.Run(() =>
        {
            var inside = Step("control.log");
            var first = Case("one", inside);
            var editor = Editor(Switch(first));

            editor.SetSelection([inside]);
            editor.GroupSelectedCommand.Execute(null);

            var body = first.StepLists.Single();
            Assert.Equal("control.sequence", Assert.Single(body.Steps).Type);
            Assert.False(editor.HasPlacementNotice);
        });
    }

    [Fact]
    public void Every_multi_list_block_adds_to_the_section_that_was_clicked()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel();
            var conditional = Step("control.if",
                Body("then"),
                Body("else"));
            var catcher = Step("control.try",
                Body("body"),
                Body("catch"),
                Body("finally"));
            var choice = Switch();
            editor.AddStep(conditional);
            editor.AddStep(catcher);
            editor.AddStep(choice);

            AddInside(editor, conditional, "then", Step("control.log"));
            AddInside(editor, conditional, "else", Step("control.note"));
            AddInside(editor, catcher, "body", Step("control.log"));
            AddInside(editor, catcher, "catch", Step("control.log"));
            AddInside(editor, catcher, "finally", Step("control.log"));
            AddInside(editor, choice, "otherwise", Step("control.log"));
            AddInside(editor, choice, "cases", Case("a"));

            Assert.Equal("control.log", Assert.Single(
                conditional.StepLists.Single(parameter => parameter.Name == "then").Steps).Type);
            Assert.Equal("control.note", Assert.Single(
                conditional.StepLists.Single(parameter => parameter.Name == "else").Steps).Type);
            Assert.All(catcher.StepLists, list => Assert.Single(list.Steps));
            Assert.Equal("control.log", Assert.Single(
                choice.StepLists.Single(parameter => parameter.Name == "otherwise").Steps).Type);
            Assert.Equal("control.case", Assert.Single(
                choice.StepLists.Single(parameter => parameter.Name == "cases").Steps).Type);
            Assert.False(editor.HasPlacementNotice);
        });

        static void AddInside(MacroEditorViewModel editor, MacroStep owner, string list, MacroStep child)
        {
            editor.AddInsideCommand.Execute(editor.Rows.Single(row =>
                row.IsHead && ReferenceEquals(row.Step, owner) && row.List?.Name == list));
            Assert.Null(editor.AddStepHere(child));
        }
    }

    private static MacroEditorViewModel Editor(MacroStep step)
    {
        var editor = new MacroEditorViewModel();
        editor.AddStep(step);
        return editor;
    }

    private static MacroStep Switch(params MacroStep[] cases)
        => Step("control.switch",
            Body("cases", cases),
            Body("otherwise"));

    private static MacroStep Case(string value, params MacroStep[] body)
        => Step("control.case",
            new StepParameter
            {
                Name = "values",
                Kind = ActionParameterKind.Text,
                Value = value,
            },
            Body("body", body));

    private static MacroStep Step(string type, params StepParameter[] parameters)
        => new() { Type = type, Parameters = [.. parameters] };

    private static StepParameter Body(string name, params MacroStep[] steps)
        => new()
        {
            Name = name,
            Kind = ActionParameterKind.Steps,
            Steps = [.. steps],
        };
}
