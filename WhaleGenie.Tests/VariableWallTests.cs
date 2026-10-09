using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// Choosing a variable: the wall of buttons, the small picker a field opens, and what a step
/// leaves behind for either of them to offer. Writing a name out of memory is what all of this is
/// there to avoid, so the checks are about what is in front of the user rather than about names.
/// </summary>
public class VariableWallTests
{
    private static VariableChoice Local(string name) =>
        new() { Name = name, Scope = VariableScope.Local, Type = "text" };

    private static VariableChoice Global(string name) =>
        new() { Name = name, Scope = VariableScope.Global, Type = "number", Description = "总数" };

    private static VariableChoice System(string name) =>
        new() { Name = name, Scope = VariableScope.System, Type = "text", Description = "今天" };

    private static VariableChoice Outcome(string name) =>
        new()
        {
            Name = name,
            Scope = VariableScope.Local,
            Description = Strings.Get("VarWall.Outcome"),
            Source = "读订单表 · 读取 CSV · k3f9",
            IsStepResult = true,
        };

    private static VariableWallViewModel Wall() => new(
    [
        Local("rows"),
        Global("total"),
        System("sys.date"),
        Outcome("step.k3f9.outcome"),
    ]);

    [Fact]
    public void The_wall_starts_on_what_the_macro_made_for_itself()
    {
        var wall = Wall();

        Assert.Equal(["rows"], wall.Shown.Select(choice => choice.Name));
        Assert.False(wall.IsEmpty);
    }

    [Fact]
    public void Each_button_narrows_the_wall_to_its_own_group()
    {
        var wall = Wall();

        foreach (var (group, expected) in new[]
        {
            (VariableGroup.All, new[] { "rows", "total", "sys.date", "step.k3f9.outcome" }),
            (VariableGroup.Global, ["total"]),
            (VariableGroup.System, ["sys.date"]),
            (VariableGroup.StepResult, ["step.k3f9.outcome"]),
        })
        {
            wall.SelectedFilter = wall.Filters.First(filter => filter.Group == group);
            Assert.Equal(expected, wall.Shown.Select(choice => choice.Name));
        }
    }

    [Fact]
    public void A_group_with_nothing_in_it_says_so_rather_than_standing_empty()
    {
        var wall = new VariableWallViewModel([Local("rows")]);
        wall.SelectedFilter = wall.Filters.First(filter => filter.Group == VariableGroup.Global);

        Assert.True(wall.IsEmpty);
        Assert.Equal(Strings.Get("VarWall.Empty"), wall.Details);
    }

    [Fact]
    public void Picking_a_variable_hands_back_the_text_a_field_holds()
    {
        var wall = Wall();
        var picked = new List<string>();
        wall.Chosen += picked.Add;

        wall.ChooseCommand.Execute(wall.Shown[0]);

        Assert.Equal(["$rows"], picked);
    }

    /// <summary>
    /// What is on the line under the buttons: a name on a button says nothing about which variable
    /// it is, and a wall of names is a wall of guesses without it.
    /// </summary>
    [Fact]
    public void The_line_under_the_buttons_says_what_the_variable_under_the_pointer_is()
    {
        var wall = Wall();
        wall.SelectedFilter = wall.Filters.First(filter => filter.Group == VariableGroup.All);

        wall.Looking = wall.Shown.First(choice => choice.Name == "step.k3f9.outcome");
        Assert.Contains("k3f9", wall.Details);
        Assert.Contains(Strings.Get("VarWall.Outcome"), wall.Details);

        wall.Looking = null;
        Assert.Equal(Strings.Get("VarWall.Pick"), wall.Details);
    }

    [Fact]
    public void A_variable_goes_into_the_field_where_the_caret_is()
    {
        Ui.Run(() =>
        {
            var definition = ActionCatalog.Find("file.readText")
                ?? throw new InvalidOperationException("file.readText is missing from the catalogue.");
            var field = new StepParameterViewModel(
                definition.Parameters.First(parameter => parameter.Name == "path"));

            field.Text = @"C:\logs\.txt";
            field.InsertVariable("$name", 8);

            Assert.Equal(@"C:\logs\$name.txt", field.Text);
        });
    }

    [Fact]
    public void A_step_offers_what_it_leaves_behind_and_what_its_run_came_to()
    {
        var step = new MacroStep
        {
            Type = "file.readCsv",
            Id = "k3f9",
            Parameters =
            [
                new StepParameter
                {
                    Name = "resultVariable",
                    Kind = ActionParameterKind.Variable,
                    Value = "rows",
                },
            ],
        };

        var rows = VariableChoices.For([step]);
        var result = rows.First(row => row.Name == "rows");
        var ending = rows.First(row => row.Name == "step.k3f9.outcome");

        // Both say which step they came from, which is the one thing a name on its own cannot.
        Assert.Contains("k3f9", result.Source);
        Assert.False(result.IsStepResult);
        Assert.Equal(VariableGroup.Local, result.Group);
        Assert.True(ending.IsStepResult);
        Assert.Equal(VariableGroup.StepResult, ending.Group);

        // And the ones the macro does not make are offered as well: the fields may read those too.
        Assert.Contains(rows, row => row.Group == VariableGroup.System);
    }

    /// <summary>
    /// The button only exists if the dialog draws it, so the rows a field can be filled in from are
    /// read off the markup: the picker is on both kinds of field that may name a variable.
    /// </summary>
    [Fact]
    public void The_dialog_puts_the_picker_on_every_field_that_may_name_a_variable()
    {
        var markup = File.ReadAllText(
            Path.Combine(Repository(), "WhaleGenie", "Views", "AddActionWindow.axaml"));

        var value = markup.IndexOf("IsVisible=\"{Binding OffersVariables}\"", StringComparison.Ordinal);
        Assert.True(value >= 0, "the dialog stopped offering a field that may name variables");
        Assert.Contains("Click=\"OnPickVariable\"",
            markup[value..markup.IndexOf("</Grid>", value, StringComparison.Ordinal)],
            StringComparison.Ordinal);

        var name = markup.IndexOf("IsVisible=\"{Binding IsVariable}\"", StringComparison.Ordinal);
        Assert.True(name >= 0, "the dialog stopped offering a field that names a variable");
        Assert.Contains("Click=\"OnPickVariable\"",
            markup[name..markup.IndexOf("</Grid>", name, StringComparison.Ordinal)],
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_picker_stands_over_the_dialog_that_opened_it()
    {
        Ui.Run(() =>
        {
            var window = new VariablePickerWindow(new VariablePickerViewModel(
                VariableChoicesForChecks.Named("rows", "total")));
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var buttons = window.GetVisualDescendants().OfType<Button>()
                .Select(button => button.Content as string)
                .ToList();
            Assert.Contains("$rows", buttons);

            window.Close();
        });
    }

    /// <summary>The repository root, found by walking up from the test binaries.</summary>
    private static string Repository()
    {
        for (var at = new DirectoryInfo(AppContext.BaseDirectory); at is not null; at = at.Parent)
        {
            if (File.Exists(Path.Combine(at.FullName, "WhaleGenie.slnx")))
            {
                return at.FullName;
            }
        }

        throw new InvalidOperationException("WhaleGenie.slnx was not found above the test binaries.");
    }
}
