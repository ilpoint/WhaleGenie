using System.Collections.Generic;
using Avalonia.Threading;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// What a result is called before the user says anything. Two steps of the same action both want to
/// call it "rows", and the second one quietly overwrites the first, so the name says which step and
/// which of its results it came from instead.
/// </summary>
public class ReturnNameTests
{
    private static AddActionViewModel Open(string key, string stepId, out AddActionWindow window,
        IReadOnlyList<VariableChoice>? variables = null)
    {
        window = new AddActionWindow(null, ActionCatalog.Definitions, variables ?? [], [],
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
    public void The_name_carries_the_note_the_action_the_result_and_the_steps_name()
    {
        Assert.Equal("Delay_k3f9", VariableNames.ForStep(string.Empty, "Delay", string.Empty, "k3f9"));
        Assert.Equal("Delay_Text_k3f9", VariableNames.ForStep(string.Empty, "Delay", "Text", "k3f9"));

        // A note written with spaces and colons in it is not something a macro can read back, and
        // Chinese is letters, so it stays.
        Assert.Equal("上个月的销量_Delay_文本_k3f9",
            VariableNames.ForStep("上个月的销量", "Delay", "文本", "k3f9"));
        Assert.Equal("a_b_Delay_Text_k3f9", VariableNames.ForStep("a: b", "Delay", "Text", "k3f9"));

        // A long note is cut short: the name still has to read at a glance.
        var many = new string('x', 30);
        Assert.Equal($"{new string('x', VariableNames.NoteLength)}_Delay_Text_k3f9",
            VariableNames.ForStep(many, "Delay", "Text", "k3f9"));
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
            var action = ActionCatalog.Find("file.readText")!.LocalName;
            var viewModel = Open("file.readText", "k3f9", out var window);

            var result = Row(viewModel, "text");

            Assert.True(result.Ticked);
            Assert.Equal(VariableNames.ForStep(string.Empty, action, result.Label, "k3f9"), result.Name);

            // The name follows the note for as long as nobody has touched it.
            viewModel.MetaComment = "上个月的销量";
            Assert.Equal(VariableNames.ForStep("上个月的销量", action, result.Label, "k3f9"), result.Name);

            window.Close();
        });
    }

    [Fact]
    public void A_result_that_is_not_the_main_one_starts_unticked_and_gets_a_name_when_it_is_ticked()
    {
        Ui.Run(() =>
        {
            var action = ActionCatalog.Find("file.readCsv")!.LocalName;
            var viewModel = Open("file.readCsv", "k3f9", out var window);

            var header = Row(viewModel, "header");
            Assert.False(header.Ticked);

            header.Ticked = true;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(VariableNames.ForStep(string.Empty, action, header.Label, "k3f9"), header.Name);

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
            var action = ActionCatalog.Find("file.readText")!.LocalName;
            var taken = VariableNames.ForStep(string.Empty, action, "文本", "k3f9");
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
            // Reading a CSV can hand back the rows and the header row, and both would become
            // variables of their own.
            var viewModel = Open("file.readCsv", "k3f9", out var window);
            foreach (var row in viewModel.ReturnsRows)
            {
                row.Ticked = true;
            }

            Dispatcher.UIThread.RunJobs();

            var rows = Row(viewModel, "rows").Name;
            var header = Row(viewModel, "header").Name;

            Assert.NotEmpty(rows);
            Assert.NotEmpty(header);
            Assert.NotEqual(rows, header, StringComparer.OrdinalIgnoreCase);

            // Both are written into the step, each under its own name.
            var written = viewModel.BuildStep().Parameters
                .First(parameter => parameter.Name == "returns");
            Assert.Equal(["rows", "header"], written.Rows.Select(row => row.Text("key")).ToList());

            window.Close();
        });
    }

    [Fact]
    public void A_step_that_is_being_edited_keeps_the_names_it_already_has()
    {
        Ui.Run(() =>
        {
            var window = new AddActionWindow(
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
