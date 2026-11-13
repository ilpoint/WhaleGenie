using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// What the rows say: a switch as a table, its empty case, and the values a branch compares against.
public partial class StepTreeTests
{
    [Fact]
    public void A_switch_reads_as_a_table_of_values()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel();

            static MacroStep Branch(string values, params MacroStep[] body)
                => Step("control.case",
                    Text("values", values),
                    new StepParameter
                    {
                        Name = "body",
                        Kind = ActionParameterKind.Steps,
                        Steps = [.. body],
                    });

            var choice = Step("control.switch",
                Text("switchValue", "$mode"),
                new StepParameter
                {
                    Name = "cases",
                    Kind = ActionParameterKind.Steps,
                    Steps = [Branch("1, 2", Step("control.note")), Branch("5", Step("control.delay"))],
                },
                new StepParameter
                {
                    Name = "otherwise",
                    Kind = ActionParameterKind.Steps,
                    Steps = [Step("control.log")],
                });
            editor.AddStep(choice);

            // One row per value, its steps straight underneath: no section collecting the branches,
            // and no node per branch, which is what the tree used to spend four levels on. The
            // table ends with the line that adds the next branch, which is the one thing a branch
            // cannot be brought in from the library as.
            Assert.Equal(
            [
                "0:Step:control.switch",
                "1:Section:control.case",
                "2:Step:control.note",
                "1:Section:control.case",
                "2:Step:control.delay",
                "1:Add:control.switch",
                "1:Section:control.switch",
                "2:Step:control.log",
            ], Shape(editor));

            // The values are the name of the row: they are the only thing that tells one branch
            // from another, so they are on the line rather than in the branch's summary.
            Assert.Equal("值 1, 2", editor.Rows[1].HeadLabel);
            Assert.Equal("值 5", editor.Rows[3].HeadLabel);
            Assert.DoesNotContain(editor.Rows, row => row.IsSection && row.List?.Name == "cases");
        });
    }

    [Fact]
    public void An_empty_switch_offers_the_line_that_adds_the_first_branch()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel();
            editor.AddStep(Step("control.switch"));

            // A section titled "branches" holding nothing says neither what a branch is nor how to
            // make one; the line that adds the first branch says both.
            Assert.DoesNotContain(editor.Rows, row => row.IsSection && row.List?.Name == "cases");

            var add = Assert.Single(editor.Rows, row => row.IsAddRow);
            Assert.Equal(1, add.Depth);
            Assert.False(string.IsNullOrEmpty(add.AddLabel));
        });
    }

    [Fact]
    public void Adding_a_branch_writes_one_and_opens_its_values_for_typing()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel();
            var choice = Step("control.switch");
            editor.AddStep(choice);

            var branch = editor.AddBranch(choice);

            Assert.NotNull(branch);
            var cases = choice.StepLists.Single(list => list.Name == "cases");
            Assert.Same(branch, Assert.Single(cases.Steps));
            Assert.Contains(branch, editor.SelectedSteps);

            // A branch is a line of values and the steps that run for them, so the values are what
            // is asked for first — not a note, and not a second trip through the action dialog.
            var row = editor.Rows.Single(candidate => ReferenceEquals(candidate.Step, branch));
            Assert.True(row.IsCase);
            Assert.True(row.IsEditingValues);
            Assert.False(row.HasCaseValues);
        });
    }

    [Fact]
    public void A_branch_value_offers_the_variable_list_while_it_is_being_typed()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var editor = (MacroEditorViewModel)window.DataContext!;
            var choice = Step("control.switch");
            editor.AddStep(choice);
            var branch = editor.AddBranch(choice)!;
            Dispatcher.UIThread.RunJobs();

            var row = editor.Rows.Single(candidate => ReferenceEquals(candidate.Step, branch));

            // A branch value is read as text with the names in it replaced, so it is the same kind
            // of field the step's own form gives a picker to — and a line typed on the row must not
            // be the one place where a name has to be spelled from memory. The chip sits in the box
            // itself now, which is also why it does not take the focus: the line stays open.
            var pickerButton = window.GetVisualDescendants().OfType<Button>()
                .Single(button => button.IsEffectivelyVisible
                    && ReferenceEquals(button.DataContext, row)
                    && button.Classes.Contains("ValueChip"));
            pickerButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();

            var picker = Assert.Single(window.OwnedWindows.OfType<ValuePickerWindow>());
            var form = (ValuePickerViewModel)picker.DataContext!;

            // Picking a name puts it in the line where the caret is — the window hands the branch
            // a shape, and the row says the name itself, with no sign in front of it. The row is
            // chosen the way a keyboard chooses it; the click that leads to the same thing is a
            // pointer test of its own.
            var picked = form.Wall.Shown.First();
            form.Wall.Looking = picked;
            form.Place(picked.Token);
            form.Confirm();
            Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();

            Assert.Contains(picked.Name, row.CaseValuesText);

            // Closing the picker leaves the line open: the person was picking a name for it, not
            // leaving it.
            Assert.True(row.IsEditingValues);

            Ui.CloseAndDiscard(window);
        });
    }

    [Fact]
    public void A_branch_takes_its_values_where_they_are_typed_and_Escape_puts_them_back()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel();
            var choice = Step("control.switch");
            editor.AddStep(choice);
            var branch = editor.AddBranch(choice)!;
            var row = editor.Rows.Single(candidate => ReferenceEquals(candidate.Step, branch));

            row.CaseValuesText = "1; 2";
            Assert.Equal("1; 2", row.CaseValues);
            Assert.True(row.HasCaseValues);

            // Escape is the one way out that does not keep what was typed.
            row.EndEditingValues(keep: false);
            Assert.False(row.IsEditingValues);
            Assert.False(row.HasCaseValues);

            row.BeginEditingValues();
            row.CaseValuesText = "3";
            row.EndEditingValues(keep: true);
            Assert.False(row.IsEditingValues);
            Assert.Equal("3", branch.Parameters.Single(parameter => parameter.Name == "values").Value);
        });
    }
}
