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

    /// <summary>
    /// WhaleGenie's own variables are the ones whose names nobody chose, so those are the ones a
    /// button says in Chinese with the code name beside it: 剪贴板 ($sys.clipboard). A macro's own
    /// variables keep the name their author gave them, and what a button puts in a field is still
    /// the code name either way.
    /// </summary>
    [Fact]
    public void A_system_variable_is_named_in_both_languages()
    {
        Ui.Run(() => InterfaceLanguage.InChinese(() =>
        {
            var wall = new VariableWallViewModel(VariableChoices.For([]));
            wall.SelectedFilter = wall.Filters.First(filter => filter.Group == VariableGroup.System);
            var clipboard = wall.Shown.First(choice => choice.Name == "sys.clipboard");

            Assert.Equal("剪贴板 ($sys.clipboard)", clipboard.Display);
            Assert.Equal("$sys.clipboard", clipboard.Token);

            string? chosen = null;
            wall.Chosen += token => chosen = token;
            wall.ChooseCommand.Execute(clipboard);
            Assert.Equal("$sys.clipboard", chosen);

            // A macro's own variable has no second name to show.
            var own = Local("rows");
            Assert.Equal("$rows", own.Display);
        }));
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
                Handed("rows", "上月订单"),
            ],
        };

        var rows = VariableChoices.For([step]);
        var result = rows.First(row => row.Name == "上月订单");
        var ending = rows.First(row => row.Name == "step.k3f9.outcome");
        var reason = rows.First(row => row.Name == "step.k3f9.error");

        // Both say which step they came from, which is the one thing a name on its own cannot.
        Assert.Contains("k3f9", result.Source);
        Assert.False(result.IsStepResult);
        Assert.Equal(VariableGroup.Local, result.Group);
        // What it holds comes from the result the step was told to hand over.
        Assert.Equal("list", result.Type);
        Assert.True(ending.IsStepResult);
        Assert.Equal(VariableGroup.StepResult, ending.Group);

        // Why it failed sits beside how it ended: a macro that treats one failure differently from
        // another branches on this one.
        Assert.True(reason.IsStepResult);
        Assert.Equal(VariableGroup.StepResult, reason.Group);

        // And the ones the macro does not make are offered as well: the fields may read those too.
        Assert.Contains(rows, row => row.Group == VariableGroup.System);

        // A result nobody ticked is not a variable at all, so it is not offered anywhere.
        var unticked = new MacroStep { Type = "file.readCsv", Id = "bbbb" };
        Assert.DoesNotContain(VariableChoices.For([unticked]),
            row => row.Group == VariableGroup.Local && !row.IsStepResult);
    }

    /// <summary>
    /// A step told to hand one result over, written the way the file writes it.
    /// </summary>
    private static StepParameter Handed(string key, string name) => new()
    {
        Name = "returns",
        Kind = ActionParameterKind.Returns,
        Rows =
        [
            new StepParameterRow
            {
                Columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["key"] = key,
                    ["name"] = name,
                },
            },
        ],
    };

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

    /// <summary>
    /// A field that reads its whole value at once may hold something no name can stand for, so the
    /// picker hands the request for the expression editor back rather than writing into anything: the
    /// dialog that owns the field is what opens that editor and keeps what it makes. What the wall
    /// hands back for a name is unchanged — the two answers have to be told apart, and the record is
    /// how.
    /// </summary>
    [Fact]
    public void The_picker_can_ask_for_a_value_worked_out_instead_of_a_name()
    {
        var asked = new List<VariablePick?>();
        var picker = new VariablePickerViewModel(VariableChoicesForChecks.Named("rows", "total"),
            offerBuilder: true);
        picker.CloseRequested += pick => asked.Add(pick);

        Assert.True(picker.OfferBuilder);

        picker.UseExpressionCommand.Execute(null);
        Assert.Equal(new VariablePick(string.Empty, true), Assert.Single(asked));

        asked.Clear();
        picker.Wall.ChooseCommand.Execute(
            picker.Wall.Shown.First(choice => choice.Name == "rows"));
        Assert.Equal(new VariablePick("$rows", false), Assert.Single(asked));
    }

    /// <summary>
    /// The button stands in the picker only where a value may be worked out: a name-writing field and
    /// one that matches a window by text are asked the same question by a different pair of buttons,
    /// and a formula written into either would mean something else.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_expression_button_stands_only_in_a_picker_that_offers_it(bool offerBuilder)
    {
        Ui.Run(() =>
        {
            var window = new VariablePickerWindow(new VariablePickerViewModel(
                VariableChoicesForChecks.Named("rows"), offerBuilder));
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // The button is in the window's markup either way and the flag decides whether it is
            // shown, which is what the question is about.
            var button = window.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(control => control.Content as string == Strings.Get("Add.UseExpression"));
            Assert.Equal(offerBuilder, button?.IsVisible ?? false);

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

    private static StepParameter Text(string name, string value)
        => new() { Name = name, Kind = ActionParameterKind.Text, Value = value };

    private static MacroStep Writes(string name, string id = "")
        => new()
        {
            Type = "control.setVariable",
            Id = id,
            Parameters = [Text("name", name), Text("value", "0")],
        };

    private static MacroStep Reads(string expression, string comment = "")
        => new()
        {
            Type = "control.log",
            Meta = new WhaleGenie.Core.Execution.StepMeta { Comment = comment },
            Parameters = [Text("message", expression)],
        };

    /// <summary>
    /// 反查：一个变量被哪几步读。按钮上只有一个名字，而"改它的名字安不安全、它还留不留"要靠这一条
    /// 回答；定义它的那一步不是读它 —— 往变量名的字段里写名字是定义。
    /// </summary>
    [Fact]
    public void A_variable_says_which_steps_read_it()
    {
        var writes = Writes("rows", "k3f9");
        var reads = Reads("这次有 $rows 行", "记一行");

        var rows = VariableChoices.For([writes, reads]).First(choice => choice.Name == "rows");

        var use = Assert.Single(rows.Uses);
        Assert.Same(reads, use.Step);
        Assert.Contains("记一行", use.Label);
        Assert.DoesNotContain(rows.Uses, item => ReferenceEquals(item.Step, writes));

        // 没人读的变量照实说没人读，而不是把定义它的那一步也算上。
        Assert.Empty(VariableChoices.For([writes]).First(choice => choice.Name == "rows").Uses);
    }

    /// <summary>那一串步骤是点得动的：点一下就是"让我看看它在哪儿"。</summary>
    [Fact]
    public void Pressing_one_of_those_steps_asks_for_it_to_be_shown()
    {
        var reads = Reads("$rows");
        var wall = new VariableWallViewModel(VariableChoices.For([Writes("rows"), reads]));
        wall.SelectedFilter = wall.Filters.First(filter => filter.Group == VariableGroup.Local);

        // 还没有停在任何一个变量上：没有可反查的东西。
        Assert.False(wall.HasUses);

        wall.Looking = wall.Shown.First(choice => choice.Token == "$rows");

        Assert.True(wall.HasUses);
        var use = Assert.Single(wall.Uses);

        MacroStep? asked = null;
        wall.Jumped += step => asked = step;
        wall.JumpCommand.Execute(use);

        Assert.Same(reads, asked);
    }

    /// <summary>
    /// 反查要真的落到那一步上：藏在折叠块里的步骤，先把块展开再选上，否则"找到了"和"看不见"是
    /// 同一件事。
    /// </summary>
    [Fact]
    public void Showing_a_step_opens_the_block_it_is_hidden_in()
    {
        Ui.Run(() =>
        {
            var inside = Reads("$rows");
            var loop = new MacroStep
            {
                Type = "control.repeat",
                Parameters =
                [
                    new StepParameter
                    {
                        Name = "body",
                        Kind = ActionParameterKind.Steps,
                        Steps = [inside],
                    },
                ],
            };

            var editor = new MacroEditorViewModel();
            editor.AddStep(loop);
            editor.SetExpanded(loop, false);
            Assert.DoesNotContain(editor.Rows, row => row.IsStep && ReferenceEquals(row.Step, inside));

            Assert.True(editor.Reveal(inside));
            Assert.True(loop.IsExpanded);
            Assert.Contains(editor.Rows, row => row.IsStep && ReferenceEquals(row.Step, inside));

            // 不在这个宏里的步骤：什么也不做，也不假装找到过。
            Assert.False(editor.Reveal(Reads("$rows")));
        });
    }

    /// <summary>算出来的东西要有地方画：那一串步骤挂在墙上，不是一个只在内存里的列表。</summary>
    [Fact]
    public void The_wall_draws_that_list()
    {
        var markup = File.ReadAllText(
            Path.Combine(Repository(), "WhaleGenie", "Views", "VariableWall.axaml"));

        Assert.Contains("ItemsSource=\"{Binding Uses}\"", markup, StringComparison.Ordinal);
        Assert.Contains("JumpCommand", markup, StringComparison.Ordinal);
    }
}
