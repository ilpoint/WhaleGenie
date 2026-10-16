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
            var every = viewModel.ReturnsRows.First(row => row.Key == "every");
            every.Ticked = true;

            var step = viewModel.BuildStep();
            var returns = step.Parameters.First(parameter => parameter.Name == "returns");

            // Only the results that were ticked: the hit the step starts with, and the whole list
            // that was asked for.
            Assert.Equal(["match", "every"], returns.Rows.Select(row => row.Text("key")).ToList());
            var hit = returns.Rows[0].Text("name");
            var all = returns.Rows[1].Text("name");
            Assert.Contains("登录", hit, StringComparison.Ordinal);
            Assert.Contains("登录", all, StringComparison.Ordinal);

            // Each result of the step has a name of its own, so two variables are written rather
            // than one being quietly overwritten by the other.
            Assert.NotEqual(hit, all, StringComparer.OrdinalIgnoreCase);
        });
    }

    /// <summary>
    /// 用模型找东西的三步与找图那三步交出同样的两样：这一处命中（默认勾上，一条记录）与全部命中
    /// （一个列表）。命中记录比找图多带的是哪一类，形状与取法与找图一样。
    /// </summary>
    [Theory]
    [InlineData("vision.detect")]
    [InlineData("vision.waitDetect")]
    [InlineData("vision.clickDetect")]
    public void A_detection_hands_over_the_same_two_things_a_picture_finder_does(string action)
    {
        Ui.Run(() =>
        {
            var viewModel = Open(action);

            var hit = viewModel.ReturnsRows.First(row => row.Key == "match");
            var every = viewModel.ReturnsRows.First(row => row.Key == "every");
            Assert.True(hit.Ticked);
            Assert.Equal(ActionReturnType.Record, hit.Result.Type);
            Assert.Equal(ActionReturnType.List, every.Result.Type);

            // 记录里多带的两个名字写在说明里，取法与别的记录一样是 field()。
            Assert.Contains("label", hit.Result.LocalHint, StringComparison.Ordinal);
            Assert.Contains("field(", hit.Result.LocalHint, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_search_hands_over_one_hit_and_however_many_were_found()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("vision.findImage");

            // The hit is the one a search nearly always wants and starts ticked; the whole list is
            // there beside it, and ticking it is the whole of "find every one of them".
            var hit = viewModel.ReturnsRows.First(row => row.Key == "match");
            var every = viewModel.ReturnsRows.First(row => row.Key == "every");

            Assert.True(hit.Ticked);
            Assert.False(every.Ticked);
            Assert.Equal(ActionReturnType.Record, hit.Result.Type);
            Assert.Equal(ActionReturnType.List, every.Result.Type);

            // What was not ticked is not written, and what was ticked is.
            var written = viewModel.BuildStep().Parameters
                .First(parameter => parameter.Name == "returns").Rows
                .Select(row => row.Text("key")).ToList();
            Assert.Equal(["match"], written);

            every.Ticked = true;
            written = viewModel.BuildStep().Parameters
                .First(parameter => parameter.Name == "returns").Rows
                .Select(row => row.Text("key")).ToList();
            Assert.Equal(["match", "every"], written);
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

    /// <summary>
    /// 一条记录带着矩形的中心之后，「返回值」页那一行说明得说清 x、y 是什么：写宏的人是从这一行
    /// 知道 field($窗口, "x") 读得到东西的，少写一句就是一条没人找得到字段。带矩形的返回值一共有
    /// 五个，各写一遍。中英两侧都算数（这一行的文字按界面语言取，两边都写过）。
    /// </summary>
    [Theory]
    [InlineData("window.info", "window")]
    [InlineData("window.list", "windows")]
    [InlineData("uia.read", "element")]
    [InlineData("vision.capture", "place")]
    [InlineData("screen.list", "displays")]
    public void The_line_about_a_record_with_a_rectangle_names_its_middle(string action, string key)
    {
        Ui.Run(() =>
        {
            var row = Open(action).ReturnsRows.First(one => one.Key == key);

            Assert.Matches(@"\bx\b", row.Hint);
            Assert.Matches(@"\by\b", row.Hint);
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
                        Row("every", "登录_全部命中"),
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

        Assert.Equal(["match", "every"], returns.Rows.Select(row => row.Text("key")).ToList());
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
                    Rows = [Row("match", "登录_命中")],
                },
            ],
        };

        // The declaration is what a name is read out of, so the editor has to find it by key.
        var definition = ActionCatalog.Find("vision.findImage")!;
        Assert.Equal("record", definition.Returns.First(result => result.Key == "match").TypeName);
        Assert.Equal("list", definition.Returns.First(result => result.Key == "every").TypeName);

        var offered = VariableChoices.For([step]);
        var result = offered.First(choice => choice.Name == "登录_命中");

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
