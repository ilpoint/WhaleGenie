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
using WhaleGenie.Core.Expressions;
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

// What a field holds: numbers, expressions, empty answers and the defaults a new step starts with.
public partial class ActionDialogTests
{
    [Fact]
    public void A_step_that_reads_the_screen_starts_with_no_rectangle()
    {
        Ui.Run(() =>
        {
            foreach (var key in new[] { "vision.capture" })
            {
                var viewModel = Open(key);

                // Nothing written down is an answer here: the step reads the whole screen. A
                // rectangle guessed in the corner would be a step that runs and looks at the wrong
                // thing without saying so.
                foreach (var name in new[] { "x", "y", "width", "height" })
                {
                    var field = viewModel.Parameters
                        .First(parameter => parameter.Definition.Name == name);

                    Assert.Equal(string.Empty, field.CurrentText);
                    Assert.Equal(Strings.Format("Common.OptionalSuffix", field.Definition.LocalLabel),
                        field.Label);

                    // An empty box is a question nobody has answered rather than a formula, so the
                    // spinner is what the field opens as.
                    Assert.True(field.IsNumberValue);
                }

                Assert.True(viewModel.CanSave, $"{key}: {viewModel.ValidationMessage}");

                // And what was left empty is left out of the file rather than written down as a
                // zero, because zero is a place a step can mean.
                MacroStep? saved = null;
                viewModel.CloseRequested += step => saved = step;
                viewModel.SaveCommand.Execute(null);

                var names = saved!.Parameters.Select(parameter => parameter.Name).ToList();
                Assert.DoesNotContain("x", names);
                Assert.DoesNotContain("y", names);
                Assert.DoesNotContain("width", names);
                Assert.DoesNotContain("height", names);
            }
        });
    }

    /// <summary>
    /// 两个框长得一样，读法却不同：一个整格是一个在运行时才算出来的值，另一个是带 $名字 的文字。
    /// 区别写在标签左边那一个小标记上：ƒ 认算式，$ 认变量名，各自的含义在悬停里。框下面那行只说
    /// 这一格自己的事——同一句话每格复述一遍，真正属于这一格的话就被埋了。
    /// </summary>
    [Fact]
    public void A_box_says_how_it_is_read_by_a_mark_beside_its_label()
    {
        Ui.Run(() =>
        {
            var (window, viewModel) = OpenWindow("ocr.findText");
            var looked = viewModel.Parameters.First(parameter => parameter.Definition.Name == "queryText");

            Assert.True(looked.TakesFormula);
            Assert.DoesNotContain(Strings.Get("Add.FieldTakesFormula"), looked.Hint,
                StringComparison.Ordinal);

            var marked = Marks(window, LucideIconKind.SquareFunction);

            Assert.Contains(looked, marked.Select(mark => mark.Field));
            Assert.All(marked, mark => Assert.True(mark.Field.TakesFormula));

            // 标记自己把那句话带在悬停里，读的人想知道的时候才看得见。
            var own = marked.First(mark => ReferenceEquals(mark.Field, looked));
            Assert.Equal(Strings.Get("Add.FieldTakesFormula"), ToolTip.GetTip(own.Icon));

            // 带 $名字 的文字不是那一种：它的左边是 $，说的也是另一句话。
            var (plainWindow, plain) = OpenWindow("file.listFiles");
            var pattern = plain.Parameters.First(parameter => parameter.Definition.Name == "pattern");

            Assert.False(pattern.TakesFormula);
            Assert.True(pattern.ShowsVariableMark);
            Assert.DoesNotContain(Strings.Get("Add.FieldIsText"), pattern.Hint,
                StringComparison.Ordinal);

            var dollars = Marks(plainWindow, LucideIconKind.Variable);

            Assert.Contains(dollars, mark => ReferenceEquals(mark.Field, pattern));
            Assert.All(dollars, mark => Assert.True(mark.Field.ShowsVariableMark));
            Assert.Equal(Strings.Get("Add.FieldIsText"),
                ToolTip.GetTip(dollars.First(mark => ReferenceEquals(mark.Field, pattern)).Icon));

            Assert.DoesNotContain(pattern, Marks(plainWindow, LucideIconKind.SquareFunction)
                .Select(mark => mark.Field));
        });
    }

    /// <summary>
    /// 标签左边的标记：哪一种图标的标记，各自挂在哪一格的模型上。
    /// </summary>
    private static List<(IconView Icon, StepParameterViewModel Field)> Marks(
        ActionEditorWindow window, LucideIconKind kind)
        => [.. window.GetVisualDescendants().OfType<IconView>()
            .Where(icon => icon.IsEffectivelyVisible && icon.Data == kind)
            .Select(icon => (icon, icon.DataContext as StepParameterViewModel))
            .Where(pair => pair.Item2 is not null)
            .Select(pair => (pair.icon, pair.Item2!))];

    /// <summary>
    /// What an empty number box means depends on the field, and the box cannot say it: a side of a
    /// region left out reaches the edge, a window's side left out keeps what it has, and a field
    /// with a starting value takes that value. Each says which one it is under the field.
    /// </summary>
    [Fact]
    public void A_number_that_may_be_left_out_says_what_an_empty_box_means()
    {
        Ui.Run(() =>
        {
            var capture = Open("vision.capture");
            foreach (var name in new[] { "width", "height" })
            {
                var field = capture.Parameters.First(parameter => parameter.Definition.Name == name);
                Assert.Equal(ActionEmptyMeans.ToEdge, field.Definition.EmptyMeans);
                Assert.Contains(Strings.Get("Add.EmptyReachesTheEdge"), field.Hint);
            }

            // The corner of the rectangle is a place, not a side: blank there is the place zero.
            foreach (var name in new[] { "x", "y" })
            {
                var field = capture.Parameters.First(parameter => parameter.Definition.Name == name);
                Assert.Equal(ActionEmptyMeans.Zero, field.Definition.EmptyMeans);
                Assert.DoesNotContain(Strings.Get("Add.EmptyReachesTheEdge"), field.Hint);
            }

            // A search region is the same question asked as a list of places.
            var region = Open("ocr.findText").Parameters
                .First(parameter => parameter.Definition.Name == "region");
            Assert.StartsWith(Strings.Get("Add.EmptyReachesTheEdge"), region.Hint);

            // A window told to take a size keeps the one it has where a side is left out.
            var move = Open("window.move").Parameters
                .First(parameter => parameter.Definition.Name == "width");
            Assert.Equal(ActionEmptyMeans.Keep, move.Definition.EmptyMeans);
            Assert.Contains(Strings.Get("Add.EmptyKeeps"), move.Hint);

            // A wait that was not given a beat of its own follows the screen it runs on.
            var look = Open("uia.waitElement").Parameters
                .First(parameter => parameter.Definition.Name == "intervalMs");
            Assert.Equal(ActionEmptyMeans.ScreenRefresh, look.Definition.EmptyMeans);
            Assert.Contains(Strings.Get("Add.EmptyScreenRefresh"), look.Hint);

            // A field with a starting value uses it when the step says nothing.
            var rows = Open("uia.readTable").Parameters
                .First(parameter => parameter.Definition.Name == "maxRows");
            Assert.Equal(ActionEmptyMeans.Unset, rows.Definition.EmptyMeans);
            Assert.Contains(Strings.Get("Add.EmptyUsesDefault"), rows.Hint);
        });
    }

    /// <summary>
    /// 同一组数字，按识别基准的不同，说的是屏幕上的位置、窗口客户区里的位置，所以这一页必须说清
    /// 是哪一种。这句话现在并进了「在哪个范围里找」这一块的说明，不再在表格下面单占一行：一句话
    /// 本来就在回答这个块的问题，跟着块走比另起一行更省地方，也不会在每处矩形的下面重复。
    /// </summary>
    [Fact]
    public void The_basis_the_numbers_are_counted_from_is_said_by_the_area_block()
    {
        Ui.Run(() =>
        {
            foreach (var (key, block) in new[]
                     {
                         ("vision.findImage", "area"),
                         ("ocr.recognize", "area"),
                         ("vision.capture", "capture"),
                         ("vision.captureWindow", "capture"),
                     })
            {
                var row = Open(key).Rows.FirstOrDefault(line => line.GroupKey == block);
                Assert.NotNull(row);
                Assert.Contains("左上角", row.GroupHint);
            }

            // 一个不读屏的步骤没有这一块，自然也没有这句话。
            Assert.DoesNotContain(Open("input.keyPress").Rows,
                row => row.GroupKey == "area");
        });
    }

    [Fact]
    public void A_number_can_be_written_as_an_expression()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.mouseMove");
            var x = viewModel.Parameters.First(parameter => parameter.Definition.Name == "x");

            Assert.True(x.IsNumberValue);
            x.WriteValue(Formula.Binary("+", Formula.Name("count"), Formula.Number(1)));

            Assert.True(x.IsNumberFormula);
            Assert.Equal("count + 1", x.CurrentText);

            // What the builder hands back decides which box the field shows: a plain number goes
            // back to the spinner, so nothing has to know which way the field was written.
            x.WriteValue("7");

            Assert.True(x.IsNumberValue);
            Assert.Equal("7", x.CurrentText);
        });
    }

    [Fact]
    public void An_expression_survives_saving_and_reopening_the_step()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.mouseMove");
            var x = viewModel.Parameters.First(parameter => parameter.Definition.Name == "x");
            x.WriteValue(Formula.Binary("+", Formula.Name("count"), Formula.Number(1)));

            MacroStep? saved = null;
            viewModel.CloseRequested += step => saved = step;
            viewModel.SaveCommand.Execute(null);

            Assert.NotNull(saved);
            Assert.Equal("「count」 + 1",
                saved!.Parameters.First(parameter => parameter.Name == "x").Value);

            var reopened = new ActionEditorWindow(saved, ActionCatalog.Definitions,
                VariableChoicesForChecks.Named("count"), []);
            reopened.Show();
            Dispatcher.UIThread.RunJobs();

            var again = ((AddActionViewModel)reopened.DataContext!).Parameters
                .First(parameter => parameter.Definition.Name == "x");
            Assert.True(again.IsNumberFormula);
            Assert.Equal("count + 1", again.CurrentText);
        });
    }

    /// <summary>
    /// 数字格里写"从一条记录里取出一个坐标"是正常的写法：写宏的时候没有哪一步真的跑过，一个名字里
    /// 装着什么形状没有人知道，所以这种读法只可能是"要跑起来才知道"，不是错误。以前这里一律把已知
    /// 名字当成数字 0，于是唯一正确的写法 field($hit, "x") 反而当场标红，说的还是"0 不是记录"。
    /// </summary>
    [Fact]
    public void A_number_read_from_a_record_is_not_marked_wrong_while_the_macro_is_being_written()
    {
        Ui.Run(() =>
        {
            var x = Number("input.mouseMove", "x", "hit");

            x.WriteValue("field($hit, \"x\")");

            Assert.False(x.HasExpressionError);
            Assert.False(x.ShowExpressionError);
            Assert.Equal(Strings.Get("Add.ExpressionWhenRun"), x.ExpressionMessage);
        });
    }

    /// <summary>
    /// 读不通的地方照旧当场标红：算式没写完、名字不存在、函数不存在、参数个数不对，这四种写宏的人
    /// 当场就能改，不必等到跑起来。
    /// </summary>
    [Fact]
    public void A_number_that_cannot_be_read_at_all_is_still_marked_while_the_macro_is_being_written()
    {
        Ui.Run(() =>
        {
            var x = Number("input.mouseMove", "x", "hit");

            x.WriteValue("$count +");
            Assert.True(x.HasExpressionError);
            Assert.NotEqual(string.Empty, x.ExpressionMessage);

            x.WriteValue("field($nowhere, \"x\")");
            Assert.True(x.HasExpressionError);
            Assert.NotEqual(Strings.Get("Add.ExpressionWhenRun"), x.ExpressionMessage);
        });
    }

    /// <summary>
    /// 一个动作的一个数字格，放在一扇名单里只有这些名字的对话框上：这两条检查问的是"这一格对一个
    /// 形状还不知道的名字说了什么"，所以名字得由检查自己给。
    /// </summary>
    private static StepParameterViewModel Number(string key, string name, params string[] variables)
    {
        var window = new ActionEditorWindow(null, ActionCatalog.Definitions,
            VariableChoicesForChecks.Named(variables), []);

        window.Show();
        Dispatcher.UIThread.RunJobs();

        var viewModel = (AddActionViewModel)window.DataContext!;
        viewModel.SelectAction(key);
        Dispatcher.UIThread.RunJobs();

        return viewModel.Parameters.First(parameter => parameter.Definition.Name == name);
    }
}
