using System.Collections.Generic;
using Avalonia.Threading;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// What a result is called before the user says anything. Two steps of the same action both want to
/// call it "rows", and the second one quietly overwrites the first, so the name says what the note
/// says and what the result is — and, when that is already spoken for, which step it came from.
/// </summary>
public class ReturnNameTests
{
    private static AddActionViewModel Open(string key, string stepId, out ActionEditorWindow window,
        IReadOnlyList<VariableChoice>? variables = null)
    {
        window = new ActionEditorWindow(null, ActionCatalog.Definitions, variables ?? [], [],
            null, null, null, stepId);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var viewModel = (AddActionViewModel)window.DataContext!;
        viewModel.SelectAction(key);
        Dispatcher.UIThread.RunJobs();
        return viewModel;
    }

    private static ReturnsRowViewModel Row(AddActionViewModel viewModel, string key)
        => viewModel.ReturnsRows.First(row => row.Key == key);

    [Fact]
    public void The_name_carries_the_note_and_what_the_result_is()
    {
        Assert.Equal("Text", VariableNames.ForStep(string.Empty, "Text", "k3f9"));

        // A note written with spaces and colons in it is not something a macro can read back, and
        // Chinese is letters, so it stays.
        Assert.Equal("上个月的销量_文本", VariableNames.ForStep("上个月的销量", "文本", "k3f9"));
        Assert.Equal("a_b_Text", VariableNames.ForStep("a: b", "Text", "k3f9"));

        // A long note is cut short: the name still has to read at a glance.
        var many = new string('x', 30);
        Assert.Equal($"{new string('x', VariableNames.NoteLength)}_Text",
            VariableNames.ForStep(many, "Text", "k3f9"));

        // Nothing to name it after at all: a step whose action hands over a result nobody named
        // still leaves it behind, under the step's own name.
        Assert.Equal("k3f9", VariableNames.ForStep(string.Empty, string.Empty, "k3f9"));
    }

    [Fact]
    public void A_name_that_is_already_spoken_for_gets_the_steps_own_name()
    {
        // The step's name is the one part no other step shares, so the action's name and the step's
        // name are only written when they are needed.
        Assert.Equal("rows_k3f9", VariableNames.ForStep(string.Empty, "rows", "k3f9", ["rows"]));
        Assert.Equal("rows_k3f9", VariableNames.ForStep("rows", string.Empty, "k3f9", ["rows", "ROWS"]));

        // And with that taken too, the name is made free the way every other free name is.
        Assert.Equal("rows_k3f9_2",
            VariableNames.ForStep(string.Empty, "rows", "k3f9", ["rows", "rows_k3f9"]));
    }

    [Fact]
    public void A_name_that_is_already_in_use_gets_a_number_after_it()
    {
        Assert.Equal("rows", VariableNames.Free("rows", ["count"]));
        Assert.Equal("rows_2", VariableNames.Free("rows", ["rows"]));
        Assert.Equal("rows_3", VariableNames.Free("rows", ["rows", "rows_2"]));

        // Names are read without caring about case, so a name in capitals is the same variable.
        Assert.Equal("rows_2", VariableNames.Free("rows", ["ROWS"]));
    }

    [Fact]
    public void A_new_step_opens_with_its_main_result_ticked_and_named()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("file.readText", "k3f9", out var window);

            var result = Row(viewModel, "text");

            Assert.True(result.Ticked);
            Assert.Equal(VariableNames.ForStep(string.Empty, result.Label, "k3f9"), result.Name);

            // The name follows the note for as long as nobody has touched it.
            viewModel.MetaComment = "上个月的销量";
            Assert.Equal(VariableNames.ForStep("上个月的销量", result.Label, "k3f9"), result.Name);

            window.Close();
        });
    }

    [Fact]
    public void A_result_that_is_not_the_main_one_starts_unticked_and_gets_a_name_when_it_is_ticked()
    {
        Ui.Run(() =>
        {
            // A search hands over the one hit it went with, and — for a step that asks for them —
            // the list of every hit there was; the list is what a macro reaches for second.
            var viewModel = Open("vision.findImage", "k3f9", out var window);

            var side = Row(viewModel, "every");
            Assert.False(side.Ticked);

            side.Ticked = true;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(VariableNames.ForStep(string.Empty, side.Label, "k3f9"), side.Name);

            // And handing nothing over at all is allowed: the step then leaves no result behind.
            foreach (var row in viewModel.ReturnsRows)
            {
                row.Ticked = false;
            }

            Dispatcher.UIThread.RunJobs();
            Assert.Null(viewModel.BuildStep().Parameters
                .FirstOrDefault(parameter => parameter.Name == "returns"));

            window.Close();
        });
    }

    [Fact]
    public void A_name_written_by_hand_stops_following_the_note()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("file.readText", "k3f9", out var window);
            var result = Row(viewModel, "text");

            result.Name = "我的名字";
            viewModel.MetaComment = "换个备注";

            Assert.True(result.IsNameChanged);
            Assert.Equal("我的名字", result.Name);

            window.Close();
        });
    }

    [Fact]
    public void A_name_that_is_already_taken_is_pointed_out_and_can_be_made_free()
    {
        Ui.Run(() =>
        {
            var taken = VariableNames.ForStep(string.Empty, "文本", "k3f9");
            var viewModel = Open("file.readText", "k3f9", out var window,
                variables: VariableChoicesForChecks.Named(taken));
            var result = Row(viewModel, "text");

            // The name the dialog hands out steers clear of what is already there; the clash the
            // line is for is the one the user writes by hand, into a name another step already uses.
            Assert.NotEqual(taken, result.Name);
            result.Name = taken;

            Assert.True(result.IsNameTaken);
            Assert.Contains(result.Name, result.NameWarning, StringComparison.Ordinal);

            result.MakeNameUnique();

            Assert.False(result.IsNameTaken);
            Assert.Equal(taken + "_2", result.Name);

            window.Close();
        });
    }

    [Fact]
    public void Two_results_of_one_step_do_not_land_on_the_same_name()
    {
        Ui.Run(() =>
        {
            // A search hands back the hit it went with and the list of every hit there was, and
            // every one of them becomes a variable of its own.
            var viewModel = Open("vision.findImage", "k3f9", out var window);
            foreach (var row in viewModel.ReturnsRows)
            {
                row.Ticked = true;
            }

            Dispatcher.UIThread.RunJobs();

            // Every one of them gets a name, and no two of them get the same one.
            var names = viewModel.ReturnsRows.Select(row => row.Name).ToList();
            Assert.All(names, name => Assert.NotEmpty(name));
            Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());

            // And each is written into the step under its own name.
            var written = viewModel.BuildStep().Parameters
                .First(parameter => parameter.Name == "returns");
            Assert.Equal(viewModel.ReturnsRows.Select(row => row.Key),
                written.Rows.Select(row => row.Text("key")).ToList());

            window.Close();
        });
    }

    [Fact]
    public void A_step_that_is_being_edited_keeps_the_names_it_already_has()
    {
        Ui.Run(() =>
        {
            var window = new ActionEditorWindow(
                new MacroStep
                {
                    Type = "file.readText",
                    Id = "aaaa",
                    Parameters =
                    [
                        new StepParameter
                        {
                            Name = "returns",
                            Kind = ActionParameterKind.Returns,
                            Rows =
                            [
                                new StepParameterRow
                                {
                                    Columns = new Dictionary<string, string>(
                                        StringComparer.OrdinalIgnoreCase)
                                    {
                                        ["key"] = "text",
                                        ["name"] = "老名字",
                                    },
                                },
                            ],
                        },
                    ],
                },
                ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            var result = Row(viewModel, "text");

            viewModel.MetaComment = "改个备注";

            Assert.True(result.Ticked);
            Assert.Equal("老名字", result.Name);

            window.Close();
        });
    }
}
