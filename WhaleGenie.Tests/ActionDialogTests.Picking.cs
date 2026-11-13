using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lucide.Avalonia;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Recording;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;
using Key = Avalonia.Input.Key;
using KeyModifiers = Avalonia.Input.KeyModifiers;

namespace WhaleGenie.Tests;

// Picking a value: the pointer, a colour, a picture, a detection, and what the pick fills in.
public partial class ActionDialogTests
{
    [Fact]
    public void A_new_step_starts_by_pausing_and_asking_if_it_fails()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.keyPress");

            MacroStep? saved = null;
            viewModel.CloseRequested += step => saved = step;
            viewModel.SaveCommand.Execute(null);

            Assert.NotNull(saved);
            Assert.Equal(StepErrorAction.AskUser, saved!.Meta.OnError);
        });
    }

    [Fact]
    public void The_pointer_shortcut_writes_plain_numbers()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.mouseMove");
            var x = viewModel.Parameters.First(parameter => parameter.Definition.Name == "x");
            x.WriteValue("$count + 1");

            Assert.True(viewModel.ApplyCursorPosition(120, 240));
            Assert.Equal("120", x.CurrentText);
            Assert.Equal("240",
                viewModel.Parameters.First(parameter => parameter.Definition.Name == "y").CurrentText);
        });
    }

    [Theory]
    [InlineData("vision.capture")]
    [InlineData("vision.clickImage")]
    [InlineData("vision.findColor")]
    [InlineData("ocr.recognize")]
    [InlineData("vision.findColors")]
    public void An_action_that_names_a_position_offers_something_to_measure_it_from(string key)
    {
        Ui.Run(() =>
        {
            var viewModel = Open(key);

            var mode = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "anchorMode");
            Assert.True(mode.IsChoice);
            Assert.Equal("识别方式", mode.Label);
            Assert.Equal("screen", mode.CurrentText);

            Assert.Equal(["screen", "window"], mode.Choices.Select(choice => choice.Value));
            Assert.Equal(["屏幕", "窗口"], mode.Choices.Select(choice => choice.Display));

            var window = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "window");
            Assert.True(window.IsWindowRule);
            Assert.False(window.Definition.Required);
        });
    }

    [Fact]
    public void A_colour_search_offers_the_colour_the_place_and_which_hit()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("vision.findColor");

            Assert.True(viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "color").IsColor);
            // 找色只管在区域里找：点位与坐标是「比色」的事，不在这里。
            Assert.DoesNotContain(viewModel.Parameters,
                parameter => parameter.Definition.Name is "x" or "y" or "place");
            Assert.Equal("5", Value(viewModel, "tolerance"));
            Assert.Equal("1", Value(viewModel, "matchIndex"));
            Assert.Equal("0", Value(viewModel, "minCoverage"));
            // 来源窗口与取画面方式不列：它们是这一步怎么取到画面的，宏拿着没有用处，
            // 每行都要看一眼的列表里多两行只为说这个，不值。
            Assert.Equal(["found", "match", "every", "coverage", "screenPoint", "windowPoint"],
                viewModel.ReturnsRows.Select(row => row.Key).ToList());
        });
    }

    /// <summary>
    /// A colour is found one pixel at a time, so every hit a colour search reports is the same
    /// size and asking for "the biggest one" could not change the answer. Offering it would be a
    /// choice that does nothing.
    /// </summary>
    [Fact]
    public void A_colour_search_is_not_offered_the_biggest_hit()
    {
        Ui.Run(() =>
        {
            var order = Open("vision.findColor").Parameters
                .First(parameter => parameter.Definition.Name == "orderBy");
            // 颜色只有四向搜索的四种数法：两个方向各两个起点，面积对一像素的命中没有意义。
            Assert.Equal(["reading", "readingBack", "readingUp", "readingBackUp"],
                order.Definition.Options);

            var picture = Open("vision.findImage").Parameters
                .First(parameter => parameter.Definition.Name == "orderBy");
            Assert.Contains("area", picture.Definition.Options);
        });
    }

    [Fact]
    public void Finding_an_image_offers_which_hit_and_the_whole_list_of_them()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("vision.findImage");

            Assert.Equal("1", Value(viewModel, "matchIndex"));
            Assert.Equal(["found", "match", "every", "screenPoint", "windowPoint"],
                viewModel.ReturnsRows.Select(row => row.Key));
        });
    }

    /// <summary>
    /// 用模型找东西的三步：先把模型文件填上才存得下去（模型是这一步唯一非它不可的东西），标签文件
    /// 与要认的类都留空也算填好了 —— 留空是"每一类都算"，而标签文件留空是"类名没名字"。
    /// </summary>
    [Fact]
    public void Naming_the_model_is_enough_to_save_a_detection()
    {
        Ui.Run(() =>
        {
            foreach (var key in new[] { "vision.detect", "vision.waitDetect", "vision.clickDetect" })
            {
                var viewModel = Open(key);
                Assert.False(viewModel.CanSave, $"{key}: 没填模型时不该存得下去");

                var model = viewModel.Parameters
                    .First(parameter => parameter.Definition.Name == "model");
                Assert.True(model.Definition.PathIntent is PathIntent.Read);
                Assert.Equal("*.onnx", model.Definition.PathFilter);

                // 标签文件与要认的类是可以留空的：一个说"类名没名字"，一个说"每一类都算"。
                foreach (var name in new[] { "labelFile", "labels", "region" })
                {
                    Assert.False(
                        viewModel.Parameters.First(p => p.Definition.Name == name).Definition.Required,
                        $"{key}.{name}");
                }

                model.Text = @"C:\models\yolov8n.onnx";
                Assert.True(viewModel.CanSave, $"{key}: {viewModel.ValidationMessage}");

                // 两个"换个做法才会动"的数各有默认值：输入尺寸 640、允许重叠 45%。
                Assert.Equal("640", Value(viewModel, "inputSize"));
                Assert.Equal("45", Value(viewModel, "iou"));
                Assert.Equal("25", Value(viewModel, "confidence"));
            }
        });
    }

    /// <summary>
    /// Looking for a picture is complete once the picture is named. "Ignore this colour" answers a
    /// question most steps never ask, so leaving it empty is not a mistake — it is what a step that
    /// does not care about it looks like, and the label says so. Treating it as still to be filled
    /// in left all three picture finders unsavable, which is what this holds shut.
    /// </summary>
    [Fact]
    public void Naming_the_picture_is_enough_to_save_a_look()
    {
        Ui.Run(() =>
        {
            foreach (var key in new[] { "vision.findImage", "vision.waitImage", "vision.clickImage" })
            {
                var viewModel = Open(key);
                var transparent = viewModel.Parameters
                    .First(parameter => parameter.Definition.Name == "transparentColor");

                Assert.Equal(
                    Strings.Format("Common.OptionalSuffix", transparent.Definition.LocalLabel),
                    transparent.Label);

                viewModel.Parameters.First(parameter => parameter.Definition.Name == "images")
                    .AddPicture(new ImageRowViewModel { Text = @"C:\images\ok.png" });

                Assert.True(viewModel.CanSave, $"{key}: {viewModel.ValidationMessage}");
            }
        });
    }

    /// <summary>
    /// 「在哪一边」要能选：四档词都在，而且表单打开就能点。它一度被写成「等兄弟填了才可填」，
    /// 而那个开关等的是**步骤列表**，于是这一格永远是灰的——没有人会去点开一个灰着的格子，
    /// 也没有任何报错。「至少隔开」与找图那格「容差」是同一回事，一起钉住。
    /// </summary>
    [Fact]
    public void A_field_that_rides_along_with_another_is_still_editable()
    {
        Ui.Run(() =>
        {
            // 开出来的窗口都关掉：这一步要在会话里连开五扇，留着它们只会把后面几条用例拖进
            // 更长的收尾里，而这跟要问的事没有关系。
            var opened = new List<ActionEditorWindow>();
            try
            {
                foreach (var key in new[] { "uia.exists", "uia.click", "uia.select", "uia.readTable" })
                {
                    var (window, viewModel) = OpenWindow(key);
                    opened.Add(window);

                    var side = viewModel.Parameters
                        .First(parameter => parameter.Definition.Name == "anchorSide");

                    Assert.True(side.IsEnabled, $"{key}: 在哪一边不能选");
                    Assert.Equal(["left", "right", "above", "below"],
                        side.Choices.Select(choice => choice.Value));
                    Assert.Equal("左边", side.Option?.Display);

                    var gap = viewModel.Parameters
                        .First(parameter => parameter.Definition.Name == "anchorGap");
                    Assert.True(gap.IsEnabled, $"{key}: 至少隔开不能填");
                }

                var (look, lookViewModel) = OpenWindow("vision.findImage");
                opened.Add(look);

                Assert.True(
                    lookViewModel.Parameters.First(parameter => parameter.Definition.Name == "tolerance")
                        .IsEnabled,
                    "容差不能填");
            }
            finally
            {
                foreach (var window in opened)
                {
                    window.Close();
                }
            }
        });
    }

    [Fact]
    public void A_length_of_time_left_empty_is_left_out_instead_of_written_as_a_zero()
    {
        Ui.Run(() =>
        {
            // 等一个条件成立：它自己的节拍仍留在步骤里，留空表示按屏幕刷新来看。
            var viewModel = Open("control.waitUntil");
            viewModel.Parameters.First(parameter => parameter.Definition.Name == "condition").Text = "true";

            var look = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "intervalMs");

            // 默认写着 200 毫秒；把这个框清空之后，这一步就按屏幕刷新一次的时间来看。
            look.NumberValue = null;

            // Nobody has answered, so the box is empty and the label says the field may be left
            // that way: a step that says nothing here follows the screen it runs on.
            Assert.Null(look.NumberValue);
            Assert.Equal(string.Empty, look.CurrentText);
            Assert.Equal(Strings.Format("Common.OptionalSuffix", look.Definition.LocalLabel),
                look.Label);
            Assert.True(viewModel.CanSave, viewModel.ValidationMessage);

            MacroStep? saved = null;
            viewModel.CloseRequested += step => saved = step;
            viewModel.SaveCommand.Execute(null);
            Assert.DoesNotContain(saved!.Parameters, parameter => parameter.Name == "intervalMs");

            // A number written in is what the step keeps, and 0 is a number rather than nothing:
            // it means looking again as soon as the look before it is done.
            look.NumberValue = 0m;
            viewModel.SaveCommand.Execute(null);
            Assert.Equal("0", Value(viewModel, "intervalMs"));
            Assert.Contains(saved!.Parameters, parameter => parameter.Name == "intervalMs");
        });
    }

    [Fact]
    public void A_block_condition_is_edited_as_rows_over_one_expression()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("control.if");
            var expression = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "condition");

            // The condition has an editor of its own: rows over the same expression, with the
            // macro's names and the language's functions offered in each side.
            Assert.True(expression.IsCondition);
            Assert.False(expression.IsExpression);
            Assert.NotNull(expression.ConditionEditor);
            Assert.Contains("spot", expression.ExpressionSuggestions);
            Assert.Contains("contains(", expression.ExpressionSuggestions);
        });
    }

    [Fact]
    public void The_catalogue_a_step_is_written_from_leaves_out_conditions_and_cases()
    {
        // A condition says what has to be true rather than doing something, and the engine only
        // ever asks one inside an if, a while or a wait, so it is not a step of its own. A case is
        // half of a branch: without a switch to belong to it would have nothing to match against.
        // The editor hands the form RunnableActions, so that is where both have to be absent.
        Assert.DoesNotContain(ActionCatalog.RunnableActions,
            action => action.Key.StartsWith("condition.", StringComparison.Ordinal));
        Assert.DoesNotContain(ActionCatalog.RunnableActions, action => action.Key == "control.case");
        Assert.Contains(ActionCatalog.RunnableActions, action => action.Key == "input.mouseClick");
        Assert.Contains(ActionCatalog.RunnableActions, action => action.Key == "control.switch");
    }

    [Fact]
    public void A_case_is_only_reachable_through_the_switch_that_owns_it()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("control.switch");
            var cases = viewModel.Parameters.First(parameter => parameter.Definition.Name == "cases");

            // The branches of a switch are steps of a block, so they are edited in the editor's
            // own list, where the switch can be seen whole. The form keeps their data for a round
            // trip, but it never shows a second editor for them.
            var otherwise = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "otherwise");

            Assert.NotNull(cases.List);
            Assert.NotNull(otherwise.List);
            Assert.DoesNotContain(viewModel.Rows,
                row => Names(row).Any(name => name is "cases" or "otherwise"));
        });
    }

    [Fact]
    public void A_restricted_list_is_named_by_the_same_rule_every_entry_uses()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("control.switch");
            var cases = viewModel.Parameters.First(parameter => parameter.Definition.Name == "cases");
            var otherwise = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "otherwise");

            Assert.Equal(["control.case"],
                StepPlacement.Allowed(cases.Definition).Select(action => action.Key));
            Assert.Contains(StepPlacement.Allowed(otherwise.Definition),
                action => action.Key == "control.log");
            Assert.DoesNotContain(StepPlacement.Allowed(otherwise.Definition),
                action => action.Key == "control.case");
        });
    }

    [Fact]
    public void A_switch_compares_two_operands_with_an_operator_from_a_list()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("control.switch");
            var value = viewModel.Parameters.First(parameter => parameter.Definition.Name == "switchValue");
            var mode = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "switchMode");

            // Both sides of a switch's comparison are operands: a variable list to pick from with
            // room to type, so a value never has to be spelled from memory. The operator is only
            // ever picked, never written out.
            Assert.True(value.IsVariable);
            Assert.False(value.IsExpression);
            Assert.True(mode.IsChoice);
            Assert.NotEmpty(mode.Choices);

            var branch = Open("control.case");
            var values = branch.Parameters.First(parameter => parameter.Definition.Name == "values");

            // The other side is an operand too, so the branch values read the same way the value
            // they are compared with does.
            Assert.True(values.IsVariable);
        });
    }

    [Fact]
    public void A_value_field_is_read_only_and_opens_no_list_when_clicked()
    {
        Ui.Run(() =>
        {
            var (window, viewModel) = OpenWindow("control.switch");
            var value = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "switchValue");
            Dispatcher.UIThread.RunJobs();

            // A value field takes no typed line: the one way in is the chip that opens the value
            // window. Clicking the field must not cover the form with a list of every name either.
            var field = window.GetVisualDescendants().OfType<TextBox>()
                .First(box => box.IsEffectivelyVisible && ReferenceEquals(box.DataContext, value));
            Assert.True(field.IsReadOnly);
            var at = field.TranslatePoint(
                new Point(field.Bounds.Width / 2, field.Bounds.Height / 2), window)!.Value;

            window.MouseDown(at, Avalonia.Input.MouseButton.Left,
                Avalonia.Input.RawInputModifiers.LeftMouseButton);
            window.MouseUp(at, Avalonia.Input.MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            value.Text = "count";
            Dispatcher.UIThread.RunJobs();
            window.MouseDown(at, Avalonia.Input.MouseButton.Left,
                Avalonia.Input.RawInputModifiers.LeftMouseButton);
            window.MouseUp(at, Avalonia.Input.MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.True(field.IsReadOnly);
            Assert.Equal("count", field.Text);

            Ui.CloseAndAnswer(window);
        });
    }
}
