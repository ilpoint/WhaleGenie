using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// What a step hands over: the dialog ticks the results the macro should keep, each is named on its
/// own, and only those are written — into the step the engine runs, into the file, and into the
/// variables the pickers offer.
/// </summary>
public class ReturnsPageTests
{
    private static AddActionViewModel Open(string key)
    {
        var viewModel = new AddActionViewModel(ActionCatalog.Definitions, [], []);
        viewModel.SelectAction(key);
        return viewModel;
    }

    [Fact]
    public void What_the_dialog_left_ticked_reaches_the_engine()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("vision.findImage");
            viewModel.MetaComment = "登录";
            var score = viewModel.ReturnsRows.First(row => row.Key == "score");
            score.Ticked = true;

            var step = viewModel.BuildStep();
            var returns = step.Parameters.First(parameter => parameter.Name == "returns");

            // Only the results that were ticked: the position the step starts with, and the score
            // that was asked for.
            Assert.Equal(["match", "score"], returns.Rows.Select(row => row.Text("key")).ToList());
            Assert.Contains("登录", returns.Rows[0].Text("name"), StringComparison.Ordinal);
            Assert.Contains(score.Label, returns.Rows[1].Text("name"), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void The_match_count_is_only_offered_when_the_step_records_every_match()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("vision.findImage");
            var recording = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "allMatches");

            var count = viewModel.ReturnsRows.First(row => row.Key == "count");
            var every = viewModel.ReturnsRows.First(row => row.Key == "list");

            // Nothing is worked out to hand over until the step is told to record every match.
            Assert.False(count.IsApplicable);
            Assert.False(every.IsApplicable);
            Assert.True(viewModel.ReturnsRows.First(row => row.Key == "match").IsApplicable);

            recording.Flag = true;
            Assert.True(count.IsApplicable);
            Assert.True(every.IsApplicable);

            // And even a result that was ticked is not written while the box it depends on is off.
            count.Ticked = true;
            Assert.Contains("count", viewModel.BuildStep().Parameters
                .First(parameter => parameter.Name == "returns").Rows.Select(row => row.Text("key")));

            recording.Flag = false;
            Assert.DoesNotContain("count", viewModel.BuildStep().Parameters
                .First(parameter => parameter.Name == "returns").Rows.Select(row => row.Text("key")));
        });
    }

    [Fact]
    public void Handing_nothing_over_leaves_no_returns_parameter_behind()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("window.exists");
            foreach (var row in viewModel.ReturnsRows)
            {
                row.Ticked = false;
            }

            Assert.DoesNotContain(viewModel.BuildStep().Parameters,
                parameter => parameter.Name == "returns");
        });
    }

    [Fact]
    public void The_results_a_step_hands_over_survive_the_file()
    {
        var step = new MacroStep
        {
            Type = "vision.findImage",
            Id = "k3f9",
            Parameters =
            [
                new StepParameter
                {
                    Name = "returns",
                    Kind = ActionParameterKind.Returns,
                    Rows =
                    [
                        Row("match", "登录_找图_位置_k3f9"),
                        Row("count", "登录_找图_命中数_k3f9"),
                    ],
                },
            ],
        };

        var written = step.ToJson().ToJsonString();
        Assert.Contains("\"returns\"", written, StringComparison.Ordinal);
        Assert.Contains("\"key\":\"match\"", written, StringComparison.Ordinal);

        var back = MacroStep.FromJson((System.Text.Json.Nodes.JsonObject)
            System.Text.Json.Nodes.JsonNode.Parse(written)!);
        var returns = back.Parameters.First(parameter => parameter.Name == "returns");

        Assert.Equal(["match", "count"], returns.Rows.Select(row => row.Text("key")).ToList());
        Assert.Equal("登录_找图_位置_k3f9", returns.Rows[0].Text("name"));
    }

    [Fact]
    public void The_variable_wall_offers_what_a_step_hands_over()
    {
        var step = new MacroStep
        {
            Type = "vision.findImage",
            Id = "k3f9",
            Parameters =
            [
                new StepParameter
                {
                    Name = "returns",
                    Kind = ActionParameterKind.Returns,
                    Rows = [Row("match", "登录_找图_位置_k3f9")],
                },
            ],
        };

        // The declaration is what a name is read out of, so the editor has to find it by key.
        var definition = ActionCatalog.Find("vision.findImage")!;
        Assert.Equal("text", definition.Returns.First(result => result.Key == "match").TypeName);
        Assert.Equal("list", definition.Returns.First(result => result.Key == "list").TypeName);
        Assert.Equal("number", definition.Returns.First(result => result.Key == "width").TypeName);

        var offered = VariableChoices.For([step]);
        var result = offered.First(choice => choice.Name == "登录_找图_位置_k3f9");

        Assert.Contains("k3f9", result.Source, StringComparison.Ordinal);
        Assert.Equal(VariableGroup.Local, result.Group);
    }

    /// <summary>
    /// Every result the catalogue says an action can hand over has to be one the engine really
    /// writes. The engine names them as plain string literals, so its own source is the list of
    /// them, and a key the catalogue invented would be a box that ticks and does nothing.
    /// </summary>
    [Fact]
    public void Every_result_the_catalogue_names_is_one_the_engine_writes()
    {
        var source = File.ReadAllText(Path.Combine(Repository(), "WhaleGenie.Core", "Execution",
            "MacroRunner.cs"));

        var unknown = new List<string>();
        foreach (var definition in ActionCatalog.Definitions)
        {
            unknown.AddRange(definition.Returns
                .Where(result => source.IndexOf($"\"{result.Key}\"", StringComparison.Ordinal) < 0)
                .Select(result => $"{definition.Key}.{result.Key}"));
        }

        Assert.Empty(unknown);
    }

    private static StepParameterRow Row(string key, string name) => new()
    {
        Columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["key"] = key,
            ["name"] = name,
        },
    };

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
