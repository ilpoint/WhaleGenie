using Avalonia.Threading;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// What a result variable is called before the user says anything. Two steps of the same action
/// both want to call it "rows", and the second one quietly overwrites the first, so the name says
/// which step it came from instead.
/// </summary>
public class OutputNameTests
{
    private static AddActionViewModel Open(string key, string stepId, out AddActionWindow window,
        IReadOnlyList<string>? variables = null)
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

    private static StepParameterViewModel Field(AddActionViewModel viewModel, string name)
        => viewModel.Parameters.First(parameter => parameter.Definition.Name == name);

    [Fact]
    public void The_name_carries_the_note_the_action_and_the_steps_name()
    {
        Assert.Equal("Delay_k3f9", VariableNames.ForStep(string.Empty, "Delay", "k3f9"));

        // A note written with spaces and colons in it is not something a macro can read back, and
        // Chinese is letters, so it stays.
        Assert.Equal("上个月的销量_Delay_k3f9",
            VariableNames.ForStep("上个月的销量", "Delay", "k3f9"));
        Assert.Equal("a_b_Delay_k3f9", VariableNames.ForStep("a: b", "Delay", "k3f9"));

        // A long note is cut short: the name still has to read at a glance.
        var many = new string('x', 30);
        Assert.Equal($"{new string('x', VariableNames.NoteLength)}_Delay_k3f9",
            VariableNames.ForStep(many, "Delay", "k3f9"));
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
    public void A_new_step_opens_with_a_name_of_its_own()
    {
        Ui.Run(() =>
        {
            var action = ActionCatalog.Find("file.readText")!.LocalName;
            var viewModel = Open("file.readText", "k3f9", out var window);

            var result = Field(viewModel, "resultVariable");

            Assert.True(result.IsOutputVariable);
            Assert.Equal(VariableNames.ForStep(string.Empty, action, "k3f9"), result.CurrentText);

            // The name follows the note for as long as nobody has touched it.
            viewModel.MetaComment = "上个月的销量";
            Assert.Equal(VariableNames.ForStep("上个月的销量", action, "k3f9"), result.CurrentText);

            window.Close();
        });
    }

    [Fact]
    public void A_name_written_by_hand_stops_following_the_note()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("file.readText", "k3f9", out var window);
            var result = Field(viewModel, "resultVariable");

            result.Text = "我的名字";
            viewModel.MetaComment = "换个备注";

            Assert.True(result.IsNameChanged);
            Assert.Equal("我的名字", result.CurrentText);

            window.Close();
        });
    }

    [Fact]
    public void A_name_that_is_already_taken_is_pointed_out_and_can_be_made_free()
    {
        Ui.Run(() =>
        {
            var action = ActionCatalog.Find("file.readText")!.LocalName;
            var taken = VariableNames.ForStep(string.Empty, action, "k3f9");
            var viewModel = Open("file.readText", "k3f9", out var window,
                variables: [taken]);
            var result = Field(viewModel, "resultVariable");

            // The name the dialog hands out steers clear of what is already there; the clash the
            // line is for is the one the user writes by hand, into a name another step already uses.
            Assert.NotEqual(taken, result.CurrentText);
            result.Text = taken;

            Assert.True(result.IsNameTaken);
            Assert.Contains(result.CurrentText, result.NameWarning, StringComparison.Ordinal);

            result.MakeNameUnique();

            Assert.False(result.IsNameTaken);
            Assert.Equal(taken + "_2", result.CurrentText);

            window.Close();
        });
    }

    [Fact]
    public void Two_results_of_one_step_do_not_land_on_the_same_name()
    {
        Ui.Run(() =>
        {
            // Reading a CSV can hand back the rows and the header row, and both are variables the
            // step creates.
            var viewModel = Open("file.readCsv", "k3f9", out var window);

            var rows = Field(viewModel, "resultVariable").CurrentText;
            var header = Field(viewModel, "headerVariable").CurrentText;

            Assert.NotEmpty(rows);
            Assert.NotEmpty(header);
            Assert.NotEqual(rows, header, StringComparer.OrdinalIgnoreCase);

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
                            Name = "resultVariable",
                            Kind = ActionParameterKind.Variable,
                            Value = "text",
                        },
                    ],
                },
                ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            var result = Field(viewModel, "resultVariable");

            viewModel.MetaComment = "改个备注";

            Assert.Equal("text", result.CurrentText);

            window.Close();
        });
    }
}
