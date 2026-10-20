using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Recording;
using WhaleGenie.Execution;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// What the "Add action" dialog does when it is driven the way a user drives it: the window is
/// built for real, the controls are found, and the values are read back through the view model.
/// </summary>
public class ActionDialogTests
{
    [Fact]
    public void A_background_input_rule_survives_the_form_and_reaches_the_engine()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.keyPress");
            var mode = viewModel.Parameters.First(parameter =>
                parameter.Definition.Name == "inputMode");
            mode.Option = mode.Choices.First(choice => choice.Value == "background");
            Dispatcher.UIThread.RunJobs();

            var window = viewModel.Parameters.First(parameter =>
                parameter.Definition.Name == "window");
            var rule = new WindowRule(Title: "Game", Process: "game", ClassName: "GameClass",
                Handle: 42, How: WindowHow.Process);
            window.ApplyWindowRule(rule);
            viewModel.Parameters.First(parameter => parameter.Definition.Name == "key")
                .WriteValue("F5");

            var step = viewModel.BuildStep();
            var saved = Assert.Single(step.Parameters,
                parameter => parameter.Name == "window");
            Assert.True(WindowRule.TryParse(saved.Value, out var written));
            Assert.Equal(rule, written);

            var executable = Assert.Single(new[] { step }.ToExecutable());
            var posted = Assert.Single(executable.Parameters,
                parameter => parameter.Name == "window");
            Assert.True(WindowRule.TryParse(posted.Text, out var reached));
            Assert.Equal(rule, reached);
        });
    }

    /// <summary>
    /// A note is part of what a step is rather than one of its settings, so it is written on the
    /// basic page, first, where every step of every action has it.
    /// </summary>
    [Fact]
    public void The_note_of_a_step_is_written_on_the_basic_page()
    {
        var markup = File.ReadAllText(Path.Combine(Repository(), "WhaleGenie", "Views",
            "ActionEditorView.axaml"));

        Assert.Contains("MetaComment", Page(markup, "OnBase"), StringComparison.Ordinal);
        Assert.DoesNotContain("MetaComment", Page(markup, "OnOther"), StringComparison.Ordinal);
    }

    /// <summary>The markup of one page of the form: its marker up to the end of its box.</summary>
    private static string Page(string markup, string page)
    {
        var start = markup.IndexOf($"IsVisible=\"{{Binding {page}}}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"the form has no {page} page");
        return markup[start..markup.IndexOf("</Border>", start, StringComparison.Ordinal)];
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

    private static AddActionViewModel Open(string key)
        => OpenWindow(key).ViewModel;

    /// <summary>
    /// The same dialog, window and all, for a check that looks at what the form draws rather than
    /// only at what the view model holds.
    /// </summary>
    private static (AddActionWindow Window, AddActionViewModel ViewModel) OpenWindow(string key)
    {
        var window = new AddActionWindow(null, ActionCatalog.Definitions,
            VariableChoicesForChecks.Named("match.x", "match.y", "spot"), []);

        window.Show();
        Dispatcher.UIThread.RunJobs();

        var viewModel = (AddActionViewModel)window.DataContext!;
        viewModel.SelectAction(key);
        Dispatcher.UIThread.RunJobs();
        return (window, viewModel);
    }

    private static string Value(AddActionViewModel viewModel, string name)
        => viewModel.Parameters.First(parameter => parameter.Definition.Name == name).CurrentText;

    /// <summary>A window is one captured rule, so its label and placeholder do not change with a
    /// second field beside it.</summary>
    [Fact]
    public void A_window_text_keeps_its_name_however_the_window_is_recognised()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("window.exists");
            var text = viewModel.Parameters.First(parameter => parameter.Definition.Name == "window");

            // A field that may be left empty says so.
            Assert.Equal(Strings.Format("Common.OptionalSuffix", text.Definition.LocalLabel),
                text.Label);
            Assert.Equal("Click the target window, then press F1", text.Placeholder);
            Assert.True(text.IsWindowRule);
        });
    }

    /// <summary>
    /// A step whose only field is a script opens on something that runs: an empty box would leave
    /// the writer to guess at the names the macro puts in reach, and those names are the whole of
    /// how a script talks to the macro. The example is written in both languages rather than
    /// translated word for word, because what it says is code.
    /// </summary>
    [Fact]
    public void A_script_step_opens_written_from_an_example()
    {
        Ui.Run(() =>
        {
            foreach (var key in new[] { "script.python", "script.csharp" })
            {
                var viewModel = Open(key);
                var field = viewModel.Parameters
                    .First(parameter => parameter.Definition.Name == "script");

                // What is in the box is the example of the language the window is in, and the step
                // is at its default rather than showing a mark for having been written in.
                Assert.Equal(field.Definition.LocalDefault, field.CurrentText);
                Assert.Contains("WhaleGenie", field.CurrentText, StringComparison.Ordinal);
                Assert.True(field.IsDefault);
            }

            // The Chinese example says the same things in Chinese, and it is what the box opens on
            // when the window is Chinese.
            var example = ActionStrings.Chinese["script.python.script.default"];
            Assert.Contains("WhaleGenie.GetProperty(\"变量名\")", example, StringComparison.Ordinal);
            Assert.Contains("WhaleGenie.SetProperty(\"变量名\"", example, StringComparison.Ordinal);
            Assert.Contains("WhaleGenie.StepExecute(\"a7k2\")", example, StringComparison.Ordinal);
            Assert.Contains("WhaleGenie.SetResult", example, StringComparison.Ordinal);
            Assert.Contains("WhaleGenie.Print(\"Hello World\")", example, StringComparison.Ordinal);

            // The C# example says the same things through the same four calls, reached through the
            // object the engine puts in the script's way.
            var csharp = ActionStrings.Chinese["script.csharp.script.default"];
            Assert.Contains("__InjectedServices.DynamicObject", csharp, StringComparison.Ordinal);
            Assert.Contains("WhaleGenie.GetProperty(\"变量名\")", csharp, StringComparison.Ordinal);
            Assert.Contains("WhaleGenie.SetProperty(\"变量名\"", csharp, StringComparison.Ordinal);
            Assert.Contains("WhaleGenie.StepExecute(\"a7k2\")", csharp, StringComparison.Ordinal);
            Assert.Contains("WhaleGenie.SetResult", csharp, StringComparison.Ordinal);
            Assert.Contains("Console.WriteLine(\"Hello World\")", csharp, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// The AutoHotkey example says the same five things the other two languages' examples say, in
    /// the way this language reaches them: what a new step opens with is the whole of how a script
    /// hears about the macro, so every call has to be written out in the box it opens on.
    /// </summary>
    [Fact]
    public void An_AutoHotkey_step_opens_written_from_an_example()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("script.ahk");
            var field = viewModel.Parameters.First(parameter => parameter.Definition.Name == "script");

            // What is in the box is the example of the language the window is in, and the step is at
            // its default rather than showing a mark for having been written in.
            Assert.Equal(field.Definition.LocalDefault, field.CurrentText);
            Assert.True(field.IsDefault);

            var example = ActionStrings.Chinese["script.ahk.script.default"];
            Assert.Contains("GetProperty(\"变量名\")", example, StringComparison.Ordinal);
            Assert.Contains("SetProperty(\"变量名\"", example, StringComparison.Ordinal);
            Assert.Contains("StepExecute(\"a7k2\")", example, StringComparison.Ordinal);
            Assert.Contains("SetResult", example, StringComparison.Ordinal);
            Assert.Contains("Print(\"Hello World\")", example, StringComparison.Ordinal);

            // Only the line that leaves a line in the log is left to run, so a step that has just
            // been added says something the first time it is run; the rest are written as comments
            // for the writer to take the semicolon off.
            var lines = example.Split('\n');
            Assert.Contains(lines, line => line == "Print(\"Hello World\")");
            foreach (var call in new[] { "GetProperty", "SetProperty", "StepExecute", "SetResult" })
            {
                Assert.Contains(lines,
                    line => line.StartsWith("; ", StringComparison.Ordinal)
                        && line.Contains(call, StringComparison.Ordinal));
            }
        });
    }

    /// <summary>
    /// A script step is either a script written in it or a file on this machine, and only the field
    /// the answer is about is drawn: a box for a script that is not being written, or a file box for
    /// a file that is not being run, would each be a question nothing reads.
    /// </summary>
    [Fact]
    public void A_script_step_draws_the_field_of_the_source_it_is_run_from()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("script.run");

            bool Shown(string name) => viewModel.Parameters
                .First(parameter => parameter.Definition.Name == name).IsApplicable;

            // Written below by default: the script box and the encoding its copy is written in are
            // both drawn, and there is no file to name.
            Assert.Equal("text", Value(viewModel, "scriptSource"));
            Assert.True(Shown("script"), "the script box is not drawn");
            Assert.True(Shown("encoding"), "the encoding is not drawn");
            Assert.False(Shown("scriptFile"), "the file box is drawn and no file is being run");

            Choose(viewModel, "kind", "custom");

            // The program to run and the ending it reads are the two things this kind needs, so
            // they are drawn as soon as it is chosen.
            Assert.True(Shown("interpreter"), "the program to run is not drawn");
            Assert.True(Shown("extension"), "the ending the program reads is not drawn");

            Choose(viewModel, "scriptSource", "file");

            Assert.False(Shown("script"), "the script box is drawn and no script is written");
            Assert.False(Shown("encoding"), "the encoding is drawn for a file");
            Assert.True(Shown("scriptFile"), "the file box is not drawn");

            // Nor an ending for the file: what is run is already on disk with the ending its author
            // gave it, and only a script written here needs to be given one.
            Assert.True(Shown("interpreter"), "the program to run is not drawn");
            Assert.False(Shown("extension"), "an ending is asked for a file that has one");
        });
    }

    /// <summary>
    /// The three text-comparison switches are tuning, and each is drawn only where it can change
    /// the answer: case sensitivity in every way, spacing only while the two texts have to be the
    /// same, and the line rule only while the text is read as a pattern.
    /// </summary>
    [Fact]
    public void Text_comparison_switches_are_drawn_where_they_are_read()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("ocr.findText");

            Assert.True(Shown(viewModel, "caseSensitive"));
            Assert.False(Shown(viewModel, "trim"));
            Assert.False(Shown(viewModel, "multiline"));

            Choose(viewModel, "matchMode", "equals");
            Dispatcher.UIThread.RunJobs();

            Assert.True(Shown(viewModel, "caseSensitive"));
            Assert.True(Shown(viewModel, "trim"));
            Assert.False(Shown(viewModel, "multiline"));

            Choose(viewModel, "matchMode", "regex");
            Dispatcher.UIThread.RunJobs();

            Assert.True(Shown(viewModel, "caseSensitive"));
            Assert.False(Shown(viewModel, "trim"));
            Assert.True(Shown(viewModel, "multiline"));

            // A condition compares text too, so the same three answers are asked for there.
            var condition = Open("condition.compare");
            var folded = condition.AdvancedRows.SelectMany(Names).ToList();
            Assert.Contains("caseSensitive", folded);
            Assert.Contains("trim", folded);
            Assert.Contains("multiline", folded);
            Assert.True(Shown(condition, "trim"));
            Assert.False(Shown(condition, "multiline"));

            Choose(condition, "compareOperator", "regex");
            Dispatcher.UIThread.RunJobs();

            Assert.False(Shown(condition, "trim"));
            Assert.True(Shown(condition, "multiline"));
        });
    }

    [Fact]
    public void A_step_that_reads_the_screen_starts_with_no_rectangle()
    {
        Ui.Run(() =>
        {
            foreach (var key in new[] { "vision.capture", "ocr.recognize" })
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
    /// Two boxes that look exactly alike are read very differently: one holds a whole value that is
    /// worked out while the macro runs, the other holds text with the names in it replaced. The
    /// form says which is which beside the label, and says it again in the line under the box.
    /// </summary>
    [Fact]
    public void A_box_that_is_read_as_one_value_carries_the_formula_mark()
    {
        Ui.Run(() =>
        {
            var (window, viewModel) = OpenWindow("ocr.findText");
            var looked = viewModel.Parameters.First(parameter => parameter.Definition.Name == "queryText");

            Assert.True(looked.TakesFormula);
            Assert.StartsWith(Strings.Get("Add.FieldTakesFormula"), looked.Hint);

            var marked = window.GetVisualDescendants().OfType<TextBlock>()
                .Where(block => block.IsEffectivelyVisible && block.Text == "ƒ")
                .Select(block => (StepParameterViewModel)block.DataContext!)
                .ToList();

            Assert.Contains(looked, marked);
            Assert.All(marked, field => Assert.True(field.TakesFormula));

            // Text with the names in it replaced is not that, and says so instead.
            var (plainWindow, plain) = OpenWindow("file.listFiles");
            var pattern = plain.Parameters.First(parameter => parameter.Definition.Name == "pattern");

            Assert.False(pattern.TakesFormula);
            Assert.StartsWith(Strings.Get("Add.FieldIsText"), pattern.Hint);
            Assert.DoesNotContain(pattern, plainWindow.GetVisualDescendants().OfType<TextBlock>()
                .Where(block => block.IsEffectivelyVisible && block.Text == "ƒ")
                .Select(block => (StepParameterViewModel)block.DataContext!));
        });
    }

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

            // A look at the screen waits one refresh when it was not given a beat of its own.
            var look = Open("vision.waitImage").Parameters
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
    /// The same four numbers say a place on the screen, a place inside a window or a place inside a
    /// control depending on what the step counts them from, so the page says which one it is — once
    /// for the step, under the corner the numbers are counted from.
    /// </summary>
    [Fact]
    public void The_numbers_that_place_a_step_say_what_they_are_counted_from()
    {
        Ui.Run(() =>
        {
            var capture = Open("vision.capture");
            var x = capture.Parameters.First(parameter => parameter.Definition.Name == "x");
            var y = capture.Parameters.First(parameter => parameter.Definition.Name == "y");
            var width = capture.Parameters.First(parameter => parameter.Definition.Name == "width");

            Assert.Equal(Strings.Format("Add.OriginNote", Strings.Get("Add.Origin.Screen")),
                x.BasisNote);
            Assert.Equal(string.Empty, y.BasisNote);
            Assert.Equal(string.Empty, width.BasisNote);

            // The line follows the origin the moment it is changed.
            Choose(capture, "anchorMode", "window");
            Assert.Equal(Strings.Format("Add.OriginNote", Strings.Get("Add.Origin.Window")),
                x.BasisNote);

            // A step that looks in places of its own says it under the list of places.
            var find = Open("vision.findImage");
            Assert.Equal(Strings.Format("Add.OriginNote", Strings.Get("Add.Origin.Screen")),
                find.Parameters.First(parameter => parameter.Definition.Name == "region").BasisNote);

            // And a step that reads nothing off the screen has no such line at all.
            Assert.All(Open("input.keyPress").Parameters,
                parameter => Assert.Equal(string.Empty, parameter.BasisNote));
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
            x.WriteValue("$count + 1");

            Assert.True(x.IsNumberFormula);
            Assert.Equal("$count + 1", x.CurrentText);

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
            x.WriteValue("$count + 1");

            MacroStep? saved = null;
            viewModel.CloseRequested += step => saved = step;
            viewModel.SaveCommand.Execute(null);

            Assert.NotNull(saved);
            Assert.Equal("$count + 1",
                saved!.Parameters.First(parameter => parameter.Name == "x").Value);

            var reopened = new AddActionWindow(saved, ActionCatalog.Definitions,
                VariableChoicesForChecks.Named("count"), []);
            reopened.Show();
            Dispatcher.UIThread.RunJobs();

            var again = ((AddActionViewModel)reopened.DataContext!).Parameters
                .First(parameter => parameter.Definition.Name == "x");
            Assert.True(again.IsNumberFormula);
            Assert.Equal("$count + 1", again.CurrentText);
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
        var window = new AddActionWindow(null, ActionCatalog.Definitions,
            VariableChoicesForChecks.Named(variables), []);

        window.Show();
        Dispatcher.UIThread.RunJobs();

        var viewModel = (AddActionViewModel)window.DataContext!;
        viewModel.SelectAction(key);
        Dispatcher.UIThread.RunJobs();

        return viewModel.Parameters.First(parameter => parameter.Definition.Name == name);
    }

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
    [InlineData("condition.colorEquals")]
    [InlineData("condition.colorsMatch")]
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
            Assert.Equal("5", Value(viewModel, "tolerance"));
            Assert.Equal("1", Value(viewModel, "matchIndex"));
            Assert.Equal("0", Value(viewModel, "minCoverage"));
            Assert.Equal(["match", "every", "coverage", "window", "method", "screenPoint",
                "windowPoint"],
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
            Assert.Equal(["reading", "score", "random"], order.Definition.Options);

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
            Assert.Equal(["match", "every", "window", "method", "screenPoint", "windowPoint"],
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
                var ignore = viewModel.Parameters
                    .First(parameter => parameter.Definition.Name == "ignoreColor");

                Assert.Equal(
                    Strings.Format("Common.OptionalSuffix", ignore.Definition.LocalLabel),
                    ignore.Label);

                viewModel.Parameters.First(parameter => parameter.Definition.Name == "images")
                    .AddPicture(new ImageRowViewModel { Text = @"C:\images\ok.png" });

                Assert.True(viewModel.CanSave, $"{key}: {viewModel.ValidationMessage}");
            }
        });
    }

    [Fact]
    public void A_length_of_time_left_empty_is_left_out_instead_of_written_as_a_zero()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("vision.waitImage");
            viewModel.Parameters.First(parameter => parameter.Definition.Name == "images")
                .AddPicture(new ImageRowViewModel { Text = @"C:\images\ok.png" });

            var look = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "intervalMs");

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
    public void A_condition_can_be_written_as_an_expression()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("condition.expression");
            var expression = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "expression");

            // The field is the one with live checking and suggestions, not a plain text box.
            Assert.True(expression.IsExpression);
            Assert.Contains("$count", expression.Placeholder);
            Assert.Contains("$spot", expression.ExpressionSuggestions);
            Assert.Contains("contains(", expression.ExpressionSuggestions);
        });
    }

    [Fact]
    public void Conditions_are_offered_inside_a_step_and_not_as_one()
    {
        Ui.Run(() =>
        {
            var window = new AddActionWindow(null, null, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;

            // A condition says what has to be true rather than doing something, and the engine
            // only ever asks one inside an if, a while or a wait, so it is not on the step list.
            Assert.DoesNotContain(viewModel.AvailableActions,
                action => action.Category == ActionCategory.Condition);
            Assert.Contains(viewModel.AvailableActions, action => action.Key == "input.mouseClick");

            Assert.Contains(ActionCatalog.Conditions,
                condition => condition.Key == "condition.imageNotExists");
            Assert.Contains(ActionCatalog.Conditions,
                condition => condition.Key == "condition.textNotExists");
            Assert.Contains(ActionCatalog.Conditions,
                condition => condition.Key == "condition.uiaNotExists");
            Assert.Contains(ActionCatalog.Conditions,
                condition => condition.Key == "condition.expression");
        });
    }

    [Fact]
    public void A_case_is_only_reachable_through_the_switch_that_owns_it()
    {
        Ui.Run(() =>
        {
            var window = new AddActionWindow(null, null, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var picker = (AddActionViewModel)window.DataContext!;

            // A case is half of a branch: without a switch to belong to it would have nothing
            // to match against, so it is never offered as a step of its own.
            Assert.DoesNotContain(picker.AvailableActions, action => action.Key == "control.case");
            Assert.Contains(picker.AvailableActions, action => action.Key == "control.switch");

            var viewModel = Open("control.switch");
            var cases = viewModel.Parameters.First(parameter => parameter.Definition.Name == "cases");

            // The branches of a switch are steps of a block, so they are edited in the editor's
            // own list, where the switch can be seen whole, and the dialog only says so. What a
            // list may take is asked of the place the step is going (the editor's `InsertChoices`),
            // not of this dialog.
            var otherwise = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "otherwise");

            Assert.True(cases.IsStepList);
            Assert.False(cases.IsConditionList);
            // A list that names itself says so on the button that adds to it, the way the editor's
            // own list does: the branches of a switch are branches, not steps.
            Assert.Equal(Strings.Get("Add.Case"), cases.List!.AddLabel);
            Assert.True(otherwise.IsStepList);
        });
    }

    [Fact]
    public void The_add_button_in_a_block_offers_only_what_that_list_takes()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("control.switch");
            var cases = viewModel.Parameters.First(parameter => parameter.Definition.Name == "cases");
            var otherwise = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "otherwise");

            // A switch's branches are branches wherever they are added from, so the button in the
            // dialog is narrowed the same way the editor's own list is. An ordinary block takes
            // anything and says so by offering nothing in particular.
            Assert.Equal(["control.case"], cases.List!.Catalog!.Select(action => action.Key));
            Assert.Null(otherwise.List!.Catalog);
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
    public void A_variable_calculation_is_one_expression()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("control.calculate");
            var value = viewModel.Parameters.First(parameter => parameter.Definition.Name == "value");

            // The operand-and-operator pieces are gone: the whole value is one expression, offered
            // the live checking and suggestions that storing a value gets, so nothing has to be
            // assembled by hand.
            Assert.True(value.IsExpression);
            Assert.NotEmpty(value.ExpressionSuggestions);
            Assert.DoesNotContain(viewModel.Parameters,
                parameter => parameter.Definition.Name is "left" or "right" or "operator");
        });
    }

    [Fact]
    public void Nothing_can_be_stored_into_a_variable_WhaleGenie_owns()
    {
        Ui.Run(() =>
        {
            // The variables WhaleGenie provides are read-only, and the rule belongs to storing a value
            // under a name rather than to one action, so a calculation is refused the same way
            // "Set Variable" is. The clipboard is written by the clipboard action, never by
            // storing into sys.clipboard.
            foreach (var key in new[] { "control.setVariable", "control.calculate", "control.listCreate" })
            {
                var viewModel = Open(key);
                viewModel.Parameters.First(parameter => parameter.Definition.Name == "name").Text =
                    "sys.clipboard";

                Assert.False(viewModel.CanSave);
                Assert.Equal(Strings.Format("Add.SystemReadOnly", "sys.clipboard"),
                    viewModel.ValidationMessage);
            }
        });
    }

    [Fact]
    public void The_steps_of_a_block_are_edited_in_the_editor_and_not_in_the_dialog()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("control.repeat");
            var body = viewModel.Parameters.First(parameter => parameter.Definition.Name == "body");

            // The dialog says where the steps of a block went instead of showing a second list
            // of the same steps, which is what used to make the same structure editable twice.
            Assert.True(body.IsStepList);
            Assert.False(body.IsConditionList);
            Assert.NotEmpty(body.StepsNote);

            // Adding one is still here: it is the one thing about the inside of a block you may
            // well want while its settings are open.
            Assert.Equal(Strings.Get("Add.AddStepToBlock"), body.List!.AddLabel);

            // A condition is the one thing a step needs that is not a step, so it stays here.
            var test = Open("control.if");
            var condition = test.Parameters.First(parameter => parameter.Definition.Name == "condition");

            Assert.True(condition.IsConditionList);
            Assert.False(condition.IsStepList);
            Assert.Same(ActionCatalog.Conditions, condition.List!.Catalog);
        });
    }

    [Fact]
    public void The_settings_of_a_block_say_they_are_about_the_block()
    {
        Ui.Run(() =>
        {
            var window = new AddActionWindow(
                new MacroStep { Type = "control.repeat" }, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;

            // Retries, timeouts and "when it fails" mean something bigger on a block: a failure
            // inside it that no step answered is the block failing, so the dialog says so.
            Assert.True(viewModel.IsBlock);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
                text => text.IsEffectivelyVisible && text.Text == Strings.Get("Add.BlockSettings"));

            // A step that does one thing is not a block, and the line is not about it.
            Assert.False(Open("control.delay").IsBlock);
        });
    }

    [Fact]
    public void A_block_can_be_given_a_step_from_its_own_dialog()
    {
        Ui.Run(() =>
        {
            var window = new AddActionWindow(
                new MacroStep { Type = "control.repeat" }, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            var body = viewModel.Parameters.First(parameter => parameter.Definition.Name == "body");
            var before = body.StepsNote;

            // The block's settings carry the line about where its steps are ordered, and beside it
            // the one thing this dialog still does to them: add another.
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
                text => text.IsEffectivelyVisible && text.Text == body.StepsNote);
            var button = Assert.Single(window.GetVisualDescendants().OfType<Button>(),
                candidate => candidate.IsEffectivelyVisible
                    && Equals(candidate.Content, body.List!.AddLabel));

            button.Command!.Execute(button.CommandParameter);
            Dispatcher.UIThread.RunJobs();

            // The action is picked in the same dialog the editor opens for a step of its own, and
            // what it returns is appended to this block's list rather than to the macro.
            var nested = window.OwnedWindows.OfType<AddActionWindow>().Single();
            var picker = (AddActionViewModel)nested.DataContext!;
            picker.SelectAction("control.delay");
            Dispatcher.UIThread.RunJobs();
            Assert.True(picker.CanSave, picker.ValidationMessage);
            picker.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("control.delay", Assert.Single(body.List!.Steps).Type);

            // The line under the settings is the count, so it has to follow what was added.
            Assert.NotEqual(before, body.StepsNote);

            MacroStep? saved = null;
            viewModel.CloseRequested += step => saved = step;
            viewModel.SaveCommand.Execute(null);

            Assert.Equal("control.delay",
                Assert.Single(saved!.StepLists.Single().Steps).Type);
        });
    }

    [Fact]
    public void A_multi_point_colour_condition_asks_for_points_and_how_they_are_met()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("condition.colorsMatch");

            Assert.True(viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "points").IsMultiline);

            var mode = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "colorMatchMode");
            Assert.True(mode.IsChoice);
            Assert.Equal(["all", "any"], mode.Choices.Select(choice => choice.Value));
            Assert.Equal("all", mode.CurrentText);
        });
    }

    /// <summary>
    /// A colour share is asked for the way a colour search is — a colour, a place, and how much of
    /// the place has to show it — and what is filled in is what the step is handed back with. The
    /// share sits on the basic page beside the colour: a number somebody has to go looking for
    /// under "advanced" is a number they will not find.
    /// </summary>
    [Fact]
    public void A_colour_share_condition_asks_for_the_colour_the_place_and_how_much()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("condition.colorCoverage");

            Assert.True(viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "color").IsColor);
            Assert.Equal("0", Value(viewModel, "minCoverage"));
            Assert.Contains("minCoverage", viewModel.Rows.Select(row => row.First.Definition.Name));

            viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "minCoverage").NumberValue = 50;

            MacroStep? saved = null;
            viewModel.CloseRequested += result => saved = result;
            viewModel.SaveCommand.Execute(null);

            Assert.Equal("50", saved!.Parameters
                .First(parameter => parameter.Name == "minCoverage").Value);
        });
    }

    /// <summary>
    /// The switch that takes the place from the pointer stands on the line of the button it belongs
    /// to rather than taking a line of its own, and with it on the pair of numbers is not asked for:
    /// a step that works wherever the pointer is carries no coordinates for anything to read as a
    /// position of its own.
    /// </summary>
    [Fact]
    public void A_click_can_be_told_to_work_at_the_pointer()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.mouseClick");
            var row = viewModel.Rows.First(candidate =>
                candidate.First.Definition.Name == "button");

            Assert.True(row.HasThird);
            Assert.Equal("useCursor", row.Third!.Definition.Name);

            MacroStep? saved = null;
            viewModel.CloseRequested += step => saved = step;

            row.Third.Flag = true;
            Dispatcher.UIThread.RunJobs();
            viewModel.SaveCommand.Execute(null);

            Assert.Equal("true", saved!.Parameters
                .First(parameter => parameter.Name == "useCursor").Value);
            Assert.DoesNotContain(saved.Parameters, parameter => parameter.Name == "x");
            Assert.DoesNotContain(saved.Parameters, parameter => parameter.Name == "y");
        });
    }

    /// <summary>
    /// A scroll has no button to stand beside, so the switch rides the line of the two numbers it
    /// replaces; and what the numbers held is put away rather than thrown away — a step told again
    /// to work at a place keeps the place it was written with.
    /// </summary>
    [Fact]
    public void A_scroll_can_be_told_to_work_at_the_pointer_too()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.mouseScroll");
            var x = viewModel.Parameters.First(parameter => parameter.Definition.Name == "x");
            var place = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "useCursor");
            var row = viewModel.Rows.First(candidate => candidate.First.Definition.Name == "x");

            Assert.True(row.HasThird);
            Assert.Same(place, row.Third);

            x.SetNumber(300);
            place.Flag = true;
            Dispatcher.UIThread.RunJobs();

            Assert.False(x.IsIncluded);
            Assert.False(row.IsApplicable);
            Assert.Equal("300", x.CurrentText);

            place.Flag = false;
            Dispatcher.UIThread.RunJobs();

            Assert.True(x.IsIncluded);
            Assert.True(row.IsApplicable);
            Assert.Equal("300", x.CurrentText);
        });
    }

    /// <summary>
    /// Ticking the switch takes the two numbers off the form as well as out of the step. A field
    /// still drawn while the step no longer writes it is worse than one that is not drawn: what it
    /// holds reads as the place the step acts on, and it is not.
    /// </summary>
    [Fact]
    public void Ticking_the_switch_takes_the_numbers_off_the_form()
    {
        Ui.Run(() =>
        {
            var window = new AddActionWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.SelectAction("input.mouseClick");
            Dispatcher.UIThread.RunJobs();

            var place = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "useCursor");
            var numbers = Line(window, "x");

            Assert.True(numbers.IsEffectivelyVisible);

            place.Flag = true;
            Dispatcher.UIThread.RunJobs();
            Assert.False(numbers.IsEffectivelyVisible);

            // A step told again to act on the numbers it was written with is drawn the way it was:
            // nothing was thrown away while they were off the form.
            place.Flag = false;
            Dispatcher.UIThread.RunJobs();
            Assert.True(numbers.IsEffectivelyVisible);
        });
    }

    /// <summary>The line of the form a field of the step is drawn on.</summary>
    private static Control Line(Window window, string name)
        => window.GetVisualDescendants().OfType<Grid>().First(grid =>
            grid.DataContext is ParameterRowViewModel row
            && row.First.Definition.Name == name);

    /// <summary>
    /// A step told to work at the pointer opens the way it was saved: the switch is on, the numbers
    /// it does not write are not drawn, and what the step will be written as says the same.
    /// </summary>
    [Fact]
    public void A_step_told_to_work_at_the_pointer_opens_that_way()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.mouseClick");
            viewModel.Parameters.First(parameter => parameter.Definition.Name == "x").SetNumber(120);
            viewModel.Parameters.First(parameter => parameter.Definition.Name == "y").SetNumber(240);
            viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "useCursor").Flag = true;
            Dispatcher.UIThread.RunJobs();

            Assert.Contains("\"useCursor\": true", viewModel.JsonPreview, StringComparison.Ordinal);
            Assert.DoesNotContain("\"x\"", viewModel.JsonPreview, StringComparison.Ordinal);

            MacroStep? saved = null;
            viewModel.CloseRequested += step => saved = step;
            viewModel.SaveCommand.Execute(null);

            var reopened = new AddActionWindow(saved, ActionCatalog.Definitions, [], []);
            reopened.Show();
            Dispatcher.UIThread.RunJobs();

            var again = (AddActionViewModel)reopened.DataContext!;

            Assert.True(again.Parameters
                .First(parameter => parameter.Definition.Name == "useCursor").Flag);
            Assert.False(Shown(again, "x"));
            Assert.False(Shown(again, "y"));
        });
    }

    [Fact]
    public void A_position_picked_on_screen_is_stored_relative_to_the_window_client()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("vision.capture");
            viewModel.Windows = new OneOpenWindow();
            Anchor(viewModel, "window");

            Assert.True(viewModel.ApplyRegion(1008, 530, 30, 40));

            // The rectangle a step reads is stored the way the engine will read it back: counted
            // from the client-area corner rather than from the screen.
            Assert.Equal("8", Value(viewModel, "x"));
            Assert.Equal("10", Value(viewModel, "y"));
            Assert.Equal("30", Value(viewModel, "width"));
            Assert.Equal("40", Value(viewModel, "height"));
        });
    }

    [Fact]
    public void The_coordinate_bases_are_only_screen_and_window()
    {
        var modes = Ui.Run(() => ActionCatalog.Definitions
            .SelectMany(definition => definition.Parameters)
            .Where(parameter => parameter.Name == "anchorMode")
            .Select(parameter => parameter.Options)
            .Distinct()
            .ToList());

        Assert.All(modes, options => Assert.Equal(["screen", "window"], options));
    }

    /// <summary>Turns the coordinate picker of a dialog onto a named window.</summary>
    private static void Choose(AddActionViewModel viewModel, string name, string value)
    {
        var choice = viewModel.Parameters.First(parameter => parameter.Definition.Name == name);
        choice.Option = choice.Choices.First(option => option.Value == value);
    }

    /// <summary>Turns the coordinate picker of a dialog onto a named window.</summary>
    private static void Anchor(AddActionViewModel viewModel, string mode)
    {
        var choice = viewModel.Parameters
            .First(parameter => parameter.Definition.Name == "anchorMode");

        choice.Option = choice.Choices.First(option => option.Value == mode);
        viewModel.Parameters
            .First(parameter => parameter.Definition.Name == "window")
            .ApplyWindowRule(new WindowRule(Title: "Notepad"));
    }

    /// <summary>
    /// One window open at 1000,500 with its client area 20 pixels further down, which is what the
    /// picture of a step's numbers is taken against.
    /// </summary>
    private sealed class OneOpenWindow : IWindowDevice
    {
        private static readonly WindowInfo Notepad = new(1, "Notepad - notes.txt",
            new ScreenPoint(1000, 500), new ScreenSize(800, 600), false, false);

        public IReadOnlyList<WindowInfo> List() => [Notepad];

        public IReadOnlyList<WindowInfo> Filter(string value, WindowMatch match, string compare,
            bool everyWhenEmpty = false, TextCompareOptions options = default)
            => Find(value, match, compare, options) is { } window ? [window] : [];

        public WindowInfo? Find(string value, WindowMatch match, string compare,
            TextCompareOptions options = default)
            => Notepad.Title.Contains(value, StringComparison.OrdinalIgnoreCase) ? Notepad : null;

        public string ProcessOf(long handle) => "notepad";

        public string ClassOf(long handle) => "Notepad";

        public bool Activate(long handle) => true;

        public bool Minimize(long handle) => true;

        public bool Maximize(long handle) => true;

        public bool Restore(long handle) => true;

        public bool Close(long handle) => true;

        public bool Move(long handle, int x, int y, int width, int height) => true;

        public ScreenPoint ClientOrigin(long handle) => new(1000, 520);

        public ScreenSize ClientSize(long handle) => new(784, 560);
    }

    /// <summary>
    /// One control on screen at 1000,500, which is what a step anchored to a control measures from.
    /// Everything asked of it except the search is answered with "yes", because this stand-in
    /// exists to show where the numbers land rather than to drive anything.
    /// </summary>
    private sealed class OneFoundElement : IUiDevice
    {
        private static readonly UiElementInfo Save = new("Save", "saveButton", "Button",
            "ButtonClass", new ScreenPoint(1000, 500), new ScreenSize(80, 24), "Notepad");

        public bool Exists(UiQuery query, int timeoutMs) => true;

        public UiElementInfo? ElementAt(int x, int y) => Save;

        public bool Click(UiQuery query, string button) => true;

        public bool Invoke(UiQuery query) => true;

        public UiElementFacts? Facts(UiQuery query) => new(true, true, Save.Name, Save.Name,
            Save.AutomationId, Save.ControlType, Save.Location, Save.Size);

        public bool FocusWindow(UiQuery query) => true;

        public bool SetText(UiQuery query, string text, bool clearFirst) => true;

        public IReadOnlyList<UiElementInfo> FindAll(UiQuery query, int limit) => [Save];

        public bool Select(UiQuery query, string text, int itemIndex) => true;

        public bool SetChecked(UiQuery query, bool? state) => true;

        public bool SetExpanded(UiQuery query, string action) => true;

        public bool ScrollIntoView(UiQuery query) => true;

        public UiTable ReadTable(UiQuery query, int limit) => UiTable.Empty;
    }
    [Fact]
    public void A_vision_step_opens_with_the_position_it_finds_ticked()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("vision.findImage");
            var result = viewModel.ReturnsRows.First(row => row.Key == "match");

            // The position is the answer such a step nearly always wants, so it is the one ticked
            // when the step is written, and it is named after the note, the action and the result.
            Assert.True(result.Ticked);
            Assert.NotEmpty(result.Name);

            // The rest are there to be asked for, not handed over by default.
            Assert.All(viewModel.ReturnsRows.Where(row => !row.IsPrimary),
                row => Assert.False(row.Ticked));
        });
    }

    [Theory]
    [InlineData("ocr.findText", "region")]
    [InlineData("vision.findImage", "region")]
    [InlineData("condition.imageExists", "region")]
    [InlineData("vision.capture", "x")]
    [InlineData("ocr.recognize", "x")]
    [InlineData("input.mouseDrag", "startX")]
    public void The_region_picker_sits_on_the_row_that_names_a_rectangle(string key, string anchor)
    {
        Ui.Run(() =>
        {
            var viewModel = Open(key);

            Assert.True(viewModel.HasRegion);

            // Where the button is written down is the line that names the rectangle, and it stands
            // on the basic page: a rectangle is dragged out on the screen, and that is what the
            // step is about rather than how it is tuned.
            var field = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == anchor);
            Assert.True(field.IsRegionAnchor);
            Assert.Contains(anchor, viewModel.Rows.SelectMany(Names));
        });
    }

    /// <summary>
    /// A drag names two points rather than one rectangle. Its picker button therefore says where
    /// the drag goes, while the other region fields keep the ordinary rectangle wording.
    /// </summary>
    [Fact]
    public void A_drag_picker_button_names_its_start_and_end()
    {
        Ui.Run(() =>
        {
            var (window, drag) = OpenWindow("input.mouseDrag");
            var dragRow = drag.Rows.First(row => row.First.Definition.Name == "startX");

            Assert.Equal(Strings.Get("Add.PickRegionDrag"), dragRow.RegionPickerLabel);
            Assert.Equal(Strings.Get("Add.PickRegionDragHint"), dragRow.RegionPickerHint);
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(),
                button => button.IsEffectivelyVisible
                    && Equals(button.Content, Strings.Get("Add.PickRegionDrag")));

            var region = Open("ocr.recognize");
            var regionRow = region.Rows.First(row => row.First.Definition.Name == "x");

            Assert.Equal(Strings.Get("Add.PickRegion"), regionRow.RegionPickerLabel);
            Assert.Contains("RegionPickerWindow.PickDragAsync", File.ReadAllText(
                Path.Combine(Repository(), "WhaleGenie", "Views", "ActionEditorView.axaml.cs")),
                StringComparison.Ordinal);
            window.Close();
        });
    }

    [Theory]
    [InlineData("input.mouseClick")]
    [InlineData("vision.getPixel")]
    [InlineData("input.keyPress")]
    public void An_action_that_only_needs_a_point_has_no_region_picker(string key)
    {
        Ui.Run(() => Assert.False(Open(key).HasRegion));
    }

    [Fact]
    public void A_rectangle_fills_the_four_numbers_a_window_step_works_on()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("window.move");

            Assert.True(viewModel.ApplyRegion(10, 20, 30, 40));
            Assert.Equal("10", Value(viewModel, "x"));
            Assert.Equal("20", Value(viewModel, "y"));
            Assert.Equal("30", Value(viewModel, "width"));
            Assert.Equal("40", Value(viewModel, "height"));
        });
    }

    /// <summary>
    /// A window bounds where a rectangle may be drawn; it is not a rectangle already drawn. The
    /// ordinary region buttons therefore pass no starting rectangle even when the step reads a
    /// window.
    /// </summary>
    [Fact]
    public void A_window_bound_region_picker_starts_with_nothing_selected()
    {
        var code = File.ReadAllText(Path.Combine(Repository(), "WhaleGenie", "Views",
            "ActionEditorView.axaml.cs"));

        Assert.Contains("RegionPickerWindow.PickAsync(HostWindow!, Devices, null, bounds)", code,
            StringComparison.Ordinal);
        Assert.DoesNotContain("RegionPickerWindow.PickAsync(HostWindow!, Devices, bounds, bounds)", code,
            StringComparison.Ordinal);
    }

    /// <summary>The mover's picker hands the target window to the frame, which moves it as it is dragged.</summary>
    [Fact]
    public void The_window_bounds_picker_moves_the_target_window()
    {
        var code = File.ReadAllText(Path.Combine(Repository(), "WhaleGenie", "Views",
            "ActionEditorView.axaml.cs"));

        Assert.Contains(
            "RegionPickerWindow.PickAsync(HostWindow!, Devices, initial, null, target.Handle)",
            code, StringComparison.Ordinal);
    }


    /// <summary>
    /// A step that reads or copies one part of the screen has the four numbers as ordinary fields,
    /// so what the region picker dragged out lands in them one by one and each of them can still be
    /// written as a formula of its own.
    /// </summary>
    [Theory]
    [InlineData("ocr.recognize")]
    [InlineData("vision.capture")]
    public void A_rectangle_fills_the_one_region_a_step_reads(string key)
    {
        Ui.Run(() =>
        {
            var viewModel = Open(key);

            Assert.True(viewModel.ApplyRegion(10, 20, 30, 40));

            Assert.Equal("10", Value(viewModel, "x"));
            Assert.Equal("20", Value(viewModel, "y"));
            Assert.Equal("30", Value(viewModel, "width"));
            Assert.Equal("40", Value(viewModel, "height"));
            Assert.Contains("x", viewModel.Rows.SelectMany(Names));
        });
    }

    /// <summary>
    /// The pattern a reading step cuts its text down by is asked for only while the reading is set to
    /// keep what a pattern matches, and a step that keeps the writing as it stands writes nothing
    /// about a pattern: one field belongs to one way of doing something, and travels with it.
    /// </summary>
    [Fact]
    public void The_pattern_a_reading_keeps_its_text_by_is_asked_for_only_when_it_is_used()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("ocr.recognize");
            var pattern = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "contentPattern");

            // Nothing about a pattern until the reading says it keeps what one matches.
            Assert.False(Shown(viewModel, "contentPattern"));

            Choose(viewModel, "content", "letters");
            Dispatcher.UIThread.RunJobs();
            Assert.False(Shown(viewModel, "contentPattern"));

            Choose(viewModel, "content", "regex");
            Dispatcher.UIThread.RunJobs();
            Assert.True(Shown(viewModel, "contentPattern"));

            // It is tuning rather than what the step is, so it is behind the fold.
            Assert.Contains("contentPattern", viewModel.AdvancedRows.SelectMany(Names));

            pattern.Text = @"单号：(\d+)";
            Dispatcher.UIThread.RunJobs();

            MacroStep? kept = null;
            viewModel.CloseRequested += step => kept = step;
            viewModel.SaveCommand.Execute(null);

            var written = kept!.Parameters.ToDictionary(parameter => parameter.Name,
                parameter => parameter.Value, StringComparer.Ordinal);
            Assert.Equal("regex", written["content"]);
            Assert.Equal(@"单号：(\d+)", written["contentPattern"]);

            // Told to keep the writing as it stands, the step carries no pattern at all.
            Choose(viewModel, "content", "text");
            Dispatcher.UIThread.RunJobs();

            Assert.DoesNotContain("contentPattern", viewModel.JsonPreview, StringComparison.Ordinal);

            MacroStep? plain = null;
            viewModel.CloseRequested += step => plain = step;
            viewModel.SaveCommand.Execute(null);

            Assert.DoesNotContain(plain!.Parameters,
                parameter => parameter.Name == "contentPattern");
        });
    }

    /// <summary>A search that may look in several places takes the rectangle as one more place.</summary>
    [Fact]
    public void A_rectangle_adds_a_place_to_a_search()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("ocr.findText");
            var region = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "region");

            Assert.True(viewModel.ApplyRegion(10, 20, 30, 40));

            var row = Assert.Single(region.Regions);
            Assert.Equal("10", row.X.Text);
            Assert.Equal("20", row.Y.Text);
            Assert.Equal("30", row.Width.Text);
            Assert.Equal("40", row.Height.Text);
        });
    }

    [Fact]
    public void A_rectangle_fills_both_ends_of_a_drag()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.mouseDrag");

            // 框选一次填满四格：起点是矩形的左上角，终点是右下角，两头照同一个「坐标基准」量。
            Assert.True(viewModel.ApplyRegion(10, 20, 30, 40));
            Assert.Equal("10", Value(viewModel, "startX"));
            Assert.Equal("20", Value(viewModel, "startY"));
            Assert.Equal("40", Value(viewModel, "endX"));
            Assert.Equal("60", Value(viewModel, "endY"));
        });
    }

    /// <summary>A drag is a direction, so its two ends stay in the order they were dragged.</summary>
    [Fact]
    public void A_drag_keeps_the_direction_it_was_dragged()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.mouseDrag");

            Assert.True(viewModel.ApplyDrag(90, 80, 10, 20));
            Assert.Equal("90", Value(viewModel, "startX"));
            Assert.Equal("80", Value(viewModel, "startY"));
            Assert.Equal("10", Value(viewModel, "endX"));
            Assert.Equal("20", Value(viewModel, "endY"));
        });
    }

    /// <summary>
    /// 一个位置只有一个答案：填了记录，旁边的数字就清空；写了数字，记录格就清空。两个一起留着，
    /// 读的人就得自己猜运行时按哪一个走。
    /// </summary>
    [Theory]
    [InlineData("input.mouseMove", "place", "x")]
    [InlineData("input.mouseClick", "place", "x")]
    [InlineData("input.mouseDoubleClick", "place", "x")]
    [InlineData("input.mouseDown", "place", "x")]
    [InlineData("input.mouseUp", "place", "x")]
    [InlineData("input.mouseScroll", "place", "x")]
    [InlineData("vision.getPixel", "place", "x")]
    [InlineData("vision.waitColor", "place", "x")]
    [InlineData("vision.capture", "rect", "x")]
    [InlineData("ocr.recognize", "rect", "x")]
    [InlineData("window.move", "rect", "x")]
    [InlineData("input.mouseDrag", "startPlace", "startX")]
    [InlineData("input.mouseDrag", "endPlace", "endX")]
    public void A_place_and_its_numbers_empty_each_other(string action, string place, string number)
    {
        Ui.Run(() =>
        {
            var viewModel = Open(action);
            var record = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == place);
            var numbers = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == number);

            numbers.SetNumber(120);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("120", numbers.CurrentText);
            Assert.Equal(string.Empty, record.CurrentText);

            record.Text = "$命中";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("$命中", record.CurrentText);
            Assert.Equal(string.Empty, numbers.CurrentText);
        });
    }

    /// <summary>
    /// 记录格里写的东西是这一步自己的一部分：填进去的那条记录就写进步骤，数字没写就不写进文件。
    /// </summary>
    [Fact]
    public void A_record_written_in_the_field_goes_into_the_step_as_it_stands()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.mouseClick");
            var record = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "place");

            record.Text = "$命中";
            Dispatcher.UIThread.RunJobs();

            MacroStep? saved = null;
            viewModel.CloseRequested += step => saved = step;
            viewModel.SaveCommand.Execute(null);

            var written = saved!.Parameters.ToDictionary(parameter => parameter.Name,
                parameter => parameter.Value, StringComparer.Ordinal);
            Assert.Equal("$命中", written["place"]);

            // 两格数字没写就是没写：这一步的位置由那条记录说，不该在文件里留下空的数字。
            Assert.DoesNotContain("x", written.Keys);
            Assert.DoesNotContain("y", written.Keys);
        });
    }

    /// <summary>
    /// 框选与「取指针位置」写下来的数就是用户要的答案，所以写完之后记录格是空的：留着它，运行时
    /// 还是旧记录说了算，屏幕上点的那一下等于白点。
    /// </summary>
    [Fact]
    public void Picking_a_rectangle_empties_the_record_fields()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.mouseDrag");
            foreach (var name in new[] { "startPlace", "endPlace" })
            {
                viewModel.Parameters.First(parameter => parameter.Definition.Name == name).Text =
                    "$旧记录";
            }

            Dispatcher.UIThread.RunJobs();
            Assert.True(viewModel.ApplyRegion(10, 20, 30, 40));

            foreach (var name in new[] { "startPlace", "endPlace" })
            {
                Assert.Equal(string.Empty, viewModel.Parameters
                    .First(parameter => parameter.Definition.Name == name).CurrentText);
            }

            Assert.Equal("10", Value(viewModel, "startX"));
            Assert.Equal("60", Value(viewModel, "endY"));

            var capture = Open("vision.capture");
            capture.Parameters.First(parameter => parameter.Definition.Name == "rect").Text = "$旧块";
            Dispatcher.UIThread.RunJobs();

            Assert.True(capture.ApplyRegion(10, 20, 30, 40));
            Assert.Equal(string.Empty, capture.Parameters
                .First(parameter => parameter.Definition.Name == "rect").CurrentText);
            Assert.Equal("30", Value(capture, "width"));
        });
    }

    /// <summary>The same for the place a pointer reading fills in, rather than a rectangle.</summary>
    [Fact]
    public void Reading_the_pointer_empties_the_record_field()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.mouseClick");
            var record = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "place");
            record.Text = "$旧记录";
            Dispatcher.UIThread.RunJobs();

            Assert.True(viewModel.ApplyCursorPosition(300, 400));

            Assert.Equal(string.Empty, record.CurrentText);
            Assert.Equal("300", Value(viewModel, "x"));
            Assert.Equal("400", Value(viewModel, "y"));
        });
    }

    [Fact]
    public void A_wait_until_is_edited_with_the_condition_editor()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("control.waitUntil");
            var condition = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "condition");

            // The wait borrows the same nested condition editor an if or a while uses, and it
            // offers nothing but conditions, so a stray action cannot be dropped in as one.
            Assert.True(condition.IsNested);
            Assert.NotNull(condition.List);
            Assert.True(condition.List!.IsCondition);
            Assert.Same(ActionCatalog.Conditions, condition.List.Catalog);

            // How long the wait may last is not the action's business: the step's own timeout
            // says it, and this action names one of 10 seconds so a fresh step is not endless.
            Assert.DoesNotContain(viewModel.Parameters,
                parameter => parameter.Definition.Name is "timeoutMs" or "onTimeout");
            Assert.Equal(10_000, viewModel.MetaTimeout.Milliseconds);
            Assert.Equal("200", Value(viewModel, "intervalMs"));
            Assert.True(viewModel.ReturnsRows
                .First(row => row.Key == "elapsed").Ticked);
        });
    }

    [Fact]
    public void A_move_offers_the_three_ways_a_pointer_can_travel()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.mouseMove");
            var style = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "style");

            Assert.True(style.IsChoice);
            Assert.Equal("direct", Value(viewModel, "style"));
            Assert.Equal(["direct", "smooth", "human"], style.Choices.Select(choice => choice.Value));
        });
    }

    [Fact]
    public void A_step_settings_section_offers_a_backoff_and_says_what_it_waits()
    {
        Ui.Run(() =>
        {
            // A step that was already told to back off opens on that choice, and the line under
            // it spells out the waits, so the setting is never a guess about what will happen.
            var step = new MacroStep
            {
                Type = "input.keyPress",
                Parameters =
                [
                    new StepParameter { Name = "key", Kind = ActionParameterKind.Text, Value = "F5" },
                ],
                Meta = new StepMeta
                {
                    RetryCount = 2,
                    RetryDelayMs = 500,
                    RetryBackoff = RetryBackoff.Doubling,
                },
            };

            var window = new AddActionWindow(step, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            Assert.Equal("doubling", viewModel.MetaRetryBackoff.Value);
            Assert.Contains("500", viewModel.RetryPlan);
            Assert.Contains("→", viewModel.RetryPlan);

            // Picking another one changes both the plan and what is written back to the step.
            var plan = viewModel.RetryPlan;
            viewModel.MetaRetryBackoff = viewModel.BackoffChoices.First(choice => choice.Value == "jitter");
            Assert.NotEqual(plan, viewModel.RetryPlan);

            MacroStep? saved = null;
            viewModel.CloseRequested += result => saved = result;
            viewModel.SaveCommand.Execute(null);

            Assert.NotNull(saved);
            Assert.Equal(RetryBackoff.Jitter, saved!.Meta.RetryBackoff);
            Assert.Equal(2, saved.Meta.RetryCount);
        });
    }

    [Fact]
    public void A_step_without_retries_says_it_is_not_retried()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.keyPress");

            Assert.Equal("fixed", viewModel.MetaRetryBackoff.Value);
            Assert.Equal(Strings.Get("Add.StepRetryPlanNone"), viewModel.RetryPlan);
        });
    }

    [Fact]
    public void A_pause_typed_into_a_step_settings_box_can_run_on_for_days()
    {
        Ui.Run(() =>
        {
            // A wait of a few hours is normal for "release this key for a while" macro work,
            // so the ceiling has to sit well past the old one hour.
            var delay = Open("control.delay");
            Assert.Equal(StepMeta.LongestPauseMs, StoredMaximum(delay, "ms"));

            // The timeouts waiting actions keep in their own settings share the same ceiling.
            var wait = Open("vision.waitImage");
            Assert.Equal(StepMeta.LongestPauseMs, wait.MetaTimeout.Maximum);
        });
    }

    private static decimal StoredMaximum(AddActionViewModel viewModel, string name)
        => viewModel.Parameters.First(parameter => parameter.Definition.Name == name)
            .Definition.Maximum;

    /// <summary>
    /// A step that waits for something is allowed a length of time before anyone touches it: the
    /// action names one and the dialog writes it into the step's own settings, where it is on
    /// screen beside the rest of them and can be changed or cleared. Nothing is waited on by any
    /// other action, so nothing else starts with a clock.
    /// </summary>
    [Fact]
    public void A_step_that_waits_starts_out_allowed_the_length_the_action_names()
    {
        Ui.Run(() =>
        {
            foreach (var key in new[]
                     {
                         "vision.waitImage", "vision.clickImage", "vision.waitColor",
                         "vision.waitStable", "ocr.clickText",
                     })
            {
                var viewModel = Open(key);
                Assert.Equal(5000, viewModel.MetaTimeout.Milliseconds);

                MacroStep? saved = null;
                viewModel.CloseRequested += result => saved = result;
                viewModel.SaveCommand.Execute(null);
                Assert.Equal(5000, saved!.Meta.TimeoutMs);
            }

            Assert.Equal(0, Open("vision.findColor").MetaTimeout.Milliseconds);
            Assert.Equal(0, Open("input.keyPress").MetaTimeout.Milliseconds);

            // A step that already exists brings its own answer, including having none: clearing the
            // field is how a wait is told to go on until the run itself is stopped.
            var cleared = Open("vision.waitImage");
            cleared.LoadFrom(new MacroStep
            {
                Type = "vision.waitImage",
                Meta = new StepMeta { TimeoutMs = 0 },
            });
            Assert.Equal(0, cleared.MetaTimeout.Milliseconds);
        });
    }

    [Fact]
    public void A_length_of_time_can_be_written_in_minutes_and_still_saves_milliseconds()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("control.delay");
            var ms = viewModel.Parameters.First(parameter => parameter.Definition.Name == "ms");

            ms.Unit = ms.DurationUnits.First(unit => unit.Key == "min");
            ms.NumberValue = 5;

            // The unit is a way of writing the number down; the macro still holds milliseconds.
            Assert.Equal("300000", Value(viewModel, "ms"));
        });
    }

    [Fact]
    public void A_time_opens_in_milliseconds_whatever_the_number_is()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("process.waitFor");
            var timeout = viewModel.MetaTimeout;

            // 10000 is 10 whole seconds, and it still opens as 10000: every time field in a step
            // reads in milliseconds, and converting is the user's own job.
            Assert.Equal("ms", timeout.Unit.Key);
            Assert.Equal(10_000m, timeout.Value);
            Assert.Equal(10_000, timeout.Milliseconds);
        });
    }

    [Fact]
    public void A_time_already_written_into_a_step_opens_in_milliseconds()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("control.delay");
            viewModel.LoadFrom(new MacroStep
            {
                Type = "control.delay",
                Parameters =
                [
                    new StepParameter
                    {
                        Name = "ms",
                        Kind = ActionParameterKind.Number,
                        Value = "300000",
                    },
                ],
            });

            var ms = viewModel.Parameters.First(parameter => parameter.Definition.Name == "ms");

            // Five minutes, and it still opens as the 300000 the step carries.
            Assert.Equal("ms", ms.Unit.Key);
            Assert.Equal(300_000m, ms.NumberValue);
            Assert.Equal("300000", Value(viewModel, "ms"));
        });
    }

    [Fact]
    public void A_time_written_as_an_expression_is_counted_in_milliseconds()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("control.delay");
            var ms = viewModel.Parameters.First(parameter => parameter.Definition.Name == "ms");

            ms.Unit = ms.DurationUnits.First(unit => unit.Key == "min");
            ms.NumberValue = 2;

            // What the expression builder is handed is the time in milliseconds rather than the 2
            // the box is showing: an expression is counted in milliseconds, so building one out of
            // the number on screen would quietly mean a different moment.
            Assert.Equal("120000", ms.CurrentText);

            ms.WriteValue("$count + 1");

            // The unit dropdown steps aside because the engine reads the expression in milliseconds.
            Assert.False(ms.IsDurationEditor);
            Assert.True(ms.IsDurationFormula);
            Assert.Equal("$count + 1", Value(viewModel, "ms"));

            // Writing a plain number in place of the expression puts the spinner back, and it reads
            // in milliseconds the same as every other time field: what is shown is the number the
            // step holds, so nothing is quietly converted.
            ms.WriteValue("120000");

            Assert.True(ms.IsNumberValue);
            Assert.Equal("ms", ms.Unit.Key);
            Assert.Equal(120_000m, ms.NumberValue);
            Assert.Equal("120000", Value(viewModel, "ms"));
        });
    }

    [Fact]
    public void Only_lengths_of_time_get_the_unit_dropdown()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.mouseClick");
            var x = viewModel.Parameters.First(parameter => parameter.Definition.Name == "x");
            var hold = viewModel.Parameters.First(parameter => parameter.Definition.Name == "holdMs");

            Assert.False(x.IsDuration);
            Assert.False(x.IsDurationEditor);
            Assert.True(hold.IsDurationEditor);
        });
    }

    [Fact]
    public void The_unit_dropdown_is_really_on_screen_for_a_length_of_time()
    {
        Ui.Run(() =>
        {
            var window = new AddActionWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.SelectAction("control.delay");
            Dispatcher.UIThread.RunJobs();

            var unitBox = window.GetVisualDescendants().OfType<ComboBox>()
                .First(box => box.DataContext is StepParameterViewModel { IsDuration: true });

            Assert.True(unitBox.IsVisible);
            var editor = (StepParameterViewModel)unitBox.DataContext!;
            Assert.Same(editor.DurationUnits, unitBox.ItemsSource);

            // The box shows the unit it is on. A unit the list does not hold would leave the box
            // looking empty, which is what a time field must never do.
            Assert.Equal(0, unitBox.SelectedIndex);

            // The step settings carry the same choice, so a long timeout is written the same way.
            // They live on a page of their own, so that page is the one opened to read them.
            viewModel.Select(viewModel.Pages.First(page => page.Key == AddActionViewModel.OtherPage));
            Dispatcher.UIThread.RunJobs();

            var settingBox = window.GetVisualDescendants().OfType<ComboBox>()
                .First(box => ReferenceEquals(box.DataContext, viewModel.MetaTimeout));
            Assert.Same(viewModel.MetaTimeout.Units, settingBox.ItemsSource);
            Assert.Equal(0, settingBox.SelectedIndex);

            // A plain number carries no unit, so its box steps out of the way. It is the one that
            // is leaving the screen that matters: a hidden box still sits in the tree.
            viewModel.SelectAction("uia.read");
            Dispatcher.UIThread.RunJobs();

            Assert.DoesNotContain(window.GetVisualDescendants().OfType<ComboBox>()
                    .Where(box => box.IsEffectivelyVisible),
                box => box.DataContext is StepParameterViewModel { IsDuration: true });
        });
    }

    [Fact]
    public void A_step_settings_pause_can_be_written_in_minutes_and_still_saves_milliseconds()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("control.delay");
            var timeout = viewModel.MetaTimeout;
            timeout.Unit = timeout.Units.First(unit => unit.Key == "min");
            timeout.Value = 5;

            MacroStep? saved = null;
            viewModel.CloseRequested += result => saved = result;
            viewModel.SaveCommand.Execute(null);

            Assert.NotNull(saved);
            Assert.Equal(300_000, saved!.Meta.TimeoutMs);
        });
    }

    [Fact]
    public void A_step_settings_pause_opens_in_milliseconds()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.keyPress");
            viewModel.LoadFrom(new MacroStep
            {
                Type = "input.keyPress",
                Meta = new StepMeta { TimeoutMs = 3_600_000 },
            });

            // An hour, and it opens as the 3600000 the step carries.
            Assert.Equal("ms", viewModel.MetaTimeout.Unit.Key);
            Assert.Equal(3_600_000m, viewModel.MetaTimeout.Value);
            Assert.Equal(3_600_000, viewModel.MetaTimeout.Milliseconds);
        });
    }

    [Fact]
    public void The_pages_hold_the_fields_a_step_is_read_a_part_at_a_time()
    {
        Ui.Run(() =>
        {
            var window = new AddActionWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.SelectAction("input.mouseMove");
            Dispatcher.UIThread.RunJobs();

            // The place is one line, x and y share the next, and how the pointer travels takes the
            // third; the advanced page holds the rest: what the coordinates are measured from, how
            // fast the pointer goes, where the input is sent, and — for a step that posts it at a
            // window — which window that is and how it is recognised. The last two are off the form
            // while the input goes to whatever is in front, but they are still the tuning of this
            // action rather than something to show without being asked for. The three
            // text-comparison switches sit there too: they are questions about how a window title is
            // recognised, not about where the pointer goes.
            Assert.Equal(5, viewModel.Rows.Count);
            Assert.Single(viewModel.AdvancedRows);
            Assert.True(viewModel.OnBase);

            // The pages are opened by their own tabs rather than by a fold, so a step's settings
            // are read in the parts they come in.
            var advanced = viewModel.Pages.First(page => page.Key == AddActionViewModel.AdvancedPage);
            Assert.True(advanced.IsPresent);
            viewModel.Select(advanced);
            Assert.True(viewModel.OnAdvanced);
        });
    }

    [Fact]
    public void The_pages_sit_above_the_fields_so_a_full_box_of_them_cannot_push_them_away()
    {
        Ui.Run(() =>
        {
            var window = new AddActionWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // Six fields, which is what used to push the fold below the bottom edge of the dialog
            // and leave a step with settings no one could reach.
            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.SelectAction("input.mouseClick");
            Dispatcher.UIThread.RunJobs();

            var tab = window.GetVisualDescendants().OfType<Button>()
                .First(button => button.Classes.Contains("PageTab") && button.IsEffectivelyVisible);

            var corner = tab.TranslatePoint(default, window);
            Assert.NotNull(corner);

            // The pages are above the fields rather than under the last of them, which is where a
            // long form used to leave the settings behind them off the bottom edge.
            var fields = window.GetVisualDescendants().OfType<Control>()
                .Where(control => control.DataContext is StepParameterViewModel
                    && control.IsEffectivelyVisible)
                .Select(control => control.TranslatePoint(default, window))
                .OfType<Point>()
                .ToList();

            Assert.NotEmpty(fields);
            Assert.All(fields, field => Assert.True(field.Y > corner!.Value.Y,
                $"the pages at {corner} are not above the field at {field}"));
            Assert.InRange(corner!.Value.Y + tab.Bounds.Height, 0, window.Bounds.Height);
        });
    }

    [Fact]
    public void A_step_that_changed_an_advanced_setting_opens_on_that_page()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.mouseMove");
            viewModel.LoadFrom(new MacroStep
            {
                Type = "input.mouseMove",
                Parameters =
                [
                    new StepParameter
                    {
                        Name = "style",
                        Kind = ActionParameterKind.Choice,
                        Value = "smooth",
                    },
                ],
            });

            // A step that posts its input somewhere is doing something unusual, so that is the page
            // it opens on rather than something to go looking for.
            Assert.True(viewModel.OnAdvanced);
            Assert.True(viewModel.Pages.First(page =>
                page.Key == AddActionViewModel.AdvancedPage).HasDot);
        });
    }

    [Fact]
    public void A_step_that_says_nothing_unusual_opens_on_what_the_step_is()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.keyPress");
            viewModel.LoadFrom(new MacroStep
            {
                Type = "input.keyPress",
                Parameters =
                [
                    new StepParameter { Name = "key", Kind = ActionParameterKind.Key, Value = "F5" },
                ],
            });

            Assert.True(viewModel.OnBase);
        });
    }

    [Fact]
    public void A_folded_setting_is_still_saved_onto_the_step()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.keyPress");
            var mode = viewModel.Parameters.First(parameter => parameter.Definition.Name == "inputMode");
            mode.Option = mode.Choices.First(choice => choice.Value == "background");

            MacroStep? saved = null;
            viewModel.CloseRequested += result => saved = result;
            viewModel.SaveCommand.Execute(null);

            Assert.NotNull(saved);
            Assert.Contains(saved!.Parameters,
               parameter => parameter.Name == "inputMode" && parameter.Value == "background");
        });
    }

    /// <summary>
    /// A step that sends its input to a window names that window with the same two fields a window
    /// action uses, and they are asked for only while that is where its input goes: the other input
    /// modes send to whatever has the focus, so the window below is never read while they are on.
    /// </summary>
    [Fact]
    public void The_window_a_step_posts_its_input_at_is_asked_for_in_that_mode_alone()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.keyPress");

            Assert.False(Shown(viewModel, "window"));

            Choose(viewModel, "inputMode", "background");
            Dispatcher.UIThread.RunJobs();

            Assert.True(Shown(viewModel, "window"));

            // The window is the first question after the input mode, on the basic page.
            var basic = viewModel.Rows.SelectMany(Names).ToList();
            Assert.Contains("window", basic);
            Assert.Contains("inputMode", basic);

            // Back to sending in front, and the two go with the mode that reads them.
            Choose(viewModel, "inputMode", "foreground");
            Dispatcher.UIThread.RunJobs();

            Assert.False(Shown(viewModel, "window"));
        });
    }

    /// <summary>
    /// A step that posts its input at a window writes down how that window is recognised, so a run
    /// reads it back the way the dialog did; and a step sent another way writes none of it, because
    /// it never reads that window at all.
    /// </summary>
    [Fact]
    public void A_step_writes_how_the_window_it_posts_to_is_recognised()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.keyPress");
            var text = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "window");

            Choose(viewModel, "inputMode", "background");
            text.ApplyWindowRule(new WindowRule(Title: @"^notepad$", How: WindowHow.TitleRegex));
            Dispatcher.UIThread.RunJobs();

            // One field carries the captured window and the way it is compared.
            Assert.True(text.IsWindowRule);

            MacroStep? posted = null;
            viewModel.CloseRequested += step => posted = step;
            viewModel.SaveCommand.Execute(null);

            var written = posted!.Parameters.ToDictionary(parameter => parameter.Name,
                parameter => parameter.Value, StringComparer.Ordinal);
            Assert.Equal("background", written["inputMode"]);
            Assert.True(WindowRule.TryParse(written["window"], out var savedRule));
            Assert.Equal(@"^notepad$", savedRule.Title);
            Assert.Equal(WindowHow.TitleRegex, savedRule.How);

            // Told again to send in front, the step carries nothing about a window it does not read.
            Choose(viewModel, "inputMode", "foreground");
            Dispatcher.UIThread.RunJobs();

            Assert.DoesNotContain("\"window\"", viewModel.JsonPreview, StringComparison.Ordinal);

            MacroStep? inFront = null;
            viewModel.CloseRequested += step => inFront = step;
            viewModel.SaveCommand.Execute(null);

            Assert.DoesNotContain(inFront!.Parameters, parameter => parameter.Name == "window");
        });
    }

    [Fact]
    public void The_knobs_of_a_search_sit_behind_the_fold_too()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("vision.findImage");
            var visible = viewModel.Rows.SelectMany(Names).ToList();
            var folded = viewModel.AdvancedRows.SelectMany(Names).ToList();

            // Which hit to take is worth having and not worth showing on every step; where the
            // picture is recognised and how alike it has to be are part of what the step is.
            // The colour of the reference picture that keeps changing is a knob of that same kind:
            // a step that leaves it alone wants the default.
            Assert.Contains("confidence", visible);
            Assert.Contains("matchIndex", folded);
            Assert.Contains("anchorMode", visible);
            Assert.Contains("ignoreColor", folded);

            // What the step is about stays in front.
            Assert.Contains("images", visible);
            Assert.Contains("region", visible);
        });
    }

    /// <summary>
    /// Pairing up features comes back with one hit, so counting the hits, choosing which one and
    /// recording the whole set have nothing behind them there. Those lines leave the form while
    /// that way is chosen and come back with the values that were typed into them: a field that
    /// stays and does nothing is how a form comes to look broken.
    /// </summary>
    [Fact]
    public void The_questions_only_a_pixel_search_can_answer_come_and_go_with_the_way()
    {
        Ui.Run(() =>
        {
            foreach (var key in new[] { "vision.findImage", "vision.waitImage", "vision.clickImage" })
            {
                var viewModel = Open(key);
                var index = viewModel.Parameters
                    .First(parameter => parameter.Definition.Name == "matchIndex");

                index.NumberValue = 3;

                // Pairing up features: only "least feature pairs" has anything to answer to.
                Choose(viewModel, "detectAlgorithm", "feature");
                Dispatcher.UIThread.RunJobs();

                Assert.True(Shown(viewModel, "minFeatures"), key);
                Assert.False(Shown(viewModel, "matchIndex"), key);
                Assert.False(Shown(viewModel, "orderBy"), key);

                // Back to a pixel comparison, and what was typed is still there.
                Choose(viewModel, "detectAlgorithm", "correlated");
                Dispatcher.UIThread.RunJobs();

                Assert.True(Shown(viewModel, "matchIndex"), key);
                Assert.True(Shown(viewModel, "orderBy"), key);
                Assert.False(Shown(viewModel, "minFeatures"), key);
                Assert.Equal("3", index.CurrentText);
            }
        });
    }

    /// <summary>Whether a line of the form is drawn at all.</summary>
    private static bool Shown(AddActionViewModel viewModel, string name)
        => viewModel.Rows.Concat(viewModel.AdvancedRows)
            .Any(row => Names(row).Contains(name) && row.IsApplicable);

    /// <summary>
    /// A checksum works out either a piece of text or a file, never both, so the form asks for one
    /// of them: the box for the other is not drawn and not written into the step, and what was
    /// typed in it comes back when the step is pointed at it again.
    /// </summary>
    [Fact]
    public void A_checksum_asks_for_a_file_only_when_it_is_working_on_one()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("data.hash");

            Assert.True(Shown(viewModel, "dataText"));
            Assert.False(Shown(viewModel, "path"));

            viewModel.Parameters.First(parameter => parameter.Definition.Name == "dataText").Text =
                "hello";
            Choose(viewModel, "hashSource", "file");
            Dispatcher.UIThread.RunJobs();

            Assert.False(Shown(viewModel, "dataText"));
            Assert.True(Shown(viewModel, "path"));

            MacroStep? saved = null;
            viewModel.CloseRequested += step => saved = step;
            viewModel.Parameters.First(parameter => parameter.Definition.Name == "path").Text =
                "report.pdf";
            viewModel.SaveCommand.Execute(null);

            var names = saved!.Parameters.Select(parameter => parameter.Name).ToList();
            Assert.Contains("path", names);
            Assert.DoesNotContain("dataText", names);

            // Back to a piece of text, and what was typed is still there rather than thrown away.
            Choose(viewModel, "hashSource", "text");
            Dispatcher.UIThread.RunJobs();

            Assert.True(Shown(viewModel, "dataText"));
            Assert.False(Shown(viewModel, "path"));
            Assert.Equal("hello",
                viewModel.Parameters.First(parameter => parameter.Definition.Name == "dataText").Text);
        });
    }

    /// <summary>
    /// What the step is saved as carries only the questions the way it was set up asks: a step that
    /// pairs up features has no room for "the third hit", not even the default one.
    /// </summary>
    [Fact]
    public void A_step_saves_only_the_fields_its_way_asks_about()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("vision.findImage");
            viewModel.Parameters.First(parameter => parameter.Definition.Name == "images").Text =
                @"C:\images\ok.png";
            viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "matchIndex").NumberValue = 3;
            Choose(viewModel, "detectAlgorithm", "feature");
            Dispatcher.UIThread.RunJobs();

            MacroStep? saved = null;
            viewModel.CloseRequested += step => saved = step;
            viewModel.SaveCommand.Execute(null);

            Assert.NotNull(saved);
            var names = saved!.Parameters.Select(parameter => parameter.Name).ToList();

            Assert.DoesNotContain("matchIndex", names);
            Assert.DoesNotContain("orderBy", names);
            Assert.Contains("detectAlgorithm", names);
        });
    }

    /// <summary>
    /// One picker answers both "which way" and "comparing what". Two lists let a step say "pair up
    /// the features, comparing pixel for pixel", which is a sentence with nothing behind it — and
    /// whichever half lost was the half doing nothing.
    /// </summary>
    [Fact]
    public void Recognising_a_picture_is_one_choice_of_four()
    {
        Ui.Run(() =>
        {
            foreach (var key in new[] { "vision.findImage", "vision.waitImage", "vision.clickImage" })
            {
                var viewModel = Open(key);
                var folded = viewModel.AdvancedRows.SelectMany(Names).ToList();

                Assert.Contains("detectAlgorithm", folded);
                Assert.DoesNotContain("method", folded);

                var algorithm = viewModel.Parameters
                    .First(parameter => parameter.Definition.Name == "detectAlgorithm");

                Assert.Equal(["normed", "correlated", "difference", "feature"],
                    algorithm.Definition.Options);
                Assert.Equal(4, algorithm.Definition.OptionLabels.Count);
            }
        });
    }

    [Fact]
    public void A_file_step_reads_in_the_order_the_job_is_done_in()
    {
        Ui.Run(() =>
        {
            // Which file, which part of it, where the answer or the data goes — and then the
            // knobs. Filling a step in is answering those questions in that order, so the dialog
            // asks them in that order and keeps the rest behind the fold.
            var reading = Open("file.readCsv");
            Assert.Equal(["path", "hasHeader"], reading.Rows.SelectMany(Names).ToList());

            var folded = reading.AdvancedRows.SelectMany(Names).ToList();
            Assert.Contains("encoding", folded);
            Assert.Contains("separator", folded);
            Assert.Contains("trimCells", folded);
            // Reading part of a file — these columns, these rows — is how the step is tuned rather
            // than what it is, so those fields are folded away with the rest.
            Assert.Contains("columns", folded);
            Assert.Contains("matchColumn", folded);
            Assert.Contains("matchValue", folded);

            // The results the step may hand over are asked for on a page of their own: they are not
            // part of what the step is, they are how the rest of the macro reaches it.
            Assert.Equal(["rows"],
                reading.ReturnsRows.Select(row => row.Key).ToList());

            var writing = Open("file.writeCsv");
            Assert.Equal(["path", "rows", "header", "writeMode"],
                writing.Rows.SelectMany(Names).ToList());
            Assert.Contains("align", writing.AdvancedRows.SelectMany(Names));
            Assert.Empty(writing.ReturnsRows);

            var listing = Open("file.listFiles");
            Assert.Equal(["folder", "pattern", "recurse"],
                listing.Rows.SelectMany(Names).ToList());
            Assert.Equal(["files"], listing.ReturnsRows.Select(row => row.Key).ToList());

            var text = Open("file.readText");
            Assert.Equal(["path"], text.Rows.SelectMany(Names).ToList());
            Assert.Equal(["text"], text.ReturnsRows.Select(row => row.Key).ToList());
        });
    }

    [Fact]
    public void The_action_picker_opens_as_searchable_groups_rather_than_one_long_list()
    {
        Ui.Run(() =>
        {
            var window = new AddActionWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            Assert.Empty(viewModel.ActionSearch);

            // The list of actions is on screen the whole time the dialog is, rather than folded
            // away once one is chosen: changing one's mind is a click and nothing moves. The
            // groups start shut, so what is on screen is the box and the headings.
            var headings = window.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Classes.Contains("GroupToggle") && button.IsEffectivelyVisible)
                .ToList();
            Assert.NotEmpty(headings);

            // The blocks are lifted out of the categories and listed together, in the order a
            // task is built in: the four things that all "repeat" only read as different from
            // each other when they stand side by side.
            var blocks = viewModel.ActionGroups.First(group => group.Key == "blocks");
            Assert.Equal(
                ["control.sequence", "control.holdInput", "control.holdBrowser",
                    "control.repeat", "control.while", "control.for",
                    "control.forEach", "control.if", "control.switch", "control.try",
                    // The switch case is a block too, and only ever offered inside a switch, so
                    // it lands after the ones a task is built from.
                    "control.case",
                    // Answering a dialog is a block for a reason of its own: a page stops where it
                    // stands until somebody answers, so the steps that raise the dialog go inside
                    // the box rather than before it.
                    "browser.dialog"],
                blocks.Actions.Select(action => action.Key));
            Assert.True(blocks.HasNote);

            // The categories are read in two parts: what a game macro reaches for, then the rest.
            // The headings that divide them hold no actions of their own, so they are told apart
            // from the groups by that.
            var sections = viewModel.ActionGroups.Where(group => group.IsSection).ToList();
            Assert.Equal(["section.game", "section.further"],
                sections.Select(section => section.Key));
            Assert.All(sections, section => Assert.Empty(section.Actions));
            Assert.All(sections, section => Assert.True(section.HasNote));

            // Everything else stays a group a category, and no block is left behind in one.
            var groups = viewModel.ActionGroups
                .Where(group => group.HasActions && group.Key is not "recent" and not "blocks")
                .ToList();
            static string PickerCategory(ActionCategory category) => category switch
            {
                ActionCategory.Ocr => nameof(ActionCategory.Vision),
                ActionCategory.Window => nameof(ActionCategory.Process),
                _ => category.ToString(),
            };

            Assert.Equal(
                viewModel.AvailableActions.Select(action => PickerCategory(action.Category))
                    .Distinct().Order().ToList(),
                groups.Select(group => group.Key).Order().ToList());
            Assert.All(groups, group => Assert.All(group.Actions,
                card => Assert.Equal(PickerCategory(card.Definition.Category), group.Key)));
            Assert.All(groups.SelectMany(group => group.Actions), action => Assert.DoesNotContain(
                action.Definition.Parameters,
                parameter => parameter.Kind is ActionParameterKind.Steps && !parameter.ConditionsOnly));

            // Over a hundred actions read as a dozen shut headings until one of them is opened or
            // a search says which one matters. ("Recently used" is the exception: it is open,
            // because a handful of actions the user just reached for is already short enough.)
            Assert.False(blocks.IsOpen);
            Assert.All(groups, group => Assert.False(group.IsOpen));
            Assert.All(groups, group => Assert.NotEmpty(group.Actions));
            Assert.NotEmpty(blocks.Actions);

            // The first part lists the categories a game macro is written out of, in the order
            // they are reached for: what the screen shows and says, then the hands, then the
            // things around the game.
            var forGames = viewModel.ActionGroups
                .SkipWhile(group => group.Key != "section.game")
                .Skip(1)
                .TakeWhile(group => !group.IsSection)
                .Select(group => group.Key)
                .ToList();
            Assert.Equal(["Vision", "Input", "Control", "Process", "System",
                "Data", "Script"], forGames);

            var search = window.GetVisualDescendants().OfType<TextBox>()
                .First(box => Equals(box.PlaceholderText, Strings.Get("Add.SearchAction")));
            Assert.True(search.IsEffectivelyVisible);

            // The dropdown the picker used to be, holding every action at once, is gone.
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<ComboBox>(),
                box => ReferenceEquals(box.ItemsSource, viewModel.AvailableActions));
        });
    }

    [Fact]
    public void Typing_in_the_search_narrows_the_catalogue_to_what_matches()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.mouseMove");

            // Something written in any of the three things a search reads — the key, the name the
            // card shows, what the action does — finds the action. The name is asked for in
            // whatever language the interface is in rather than a word typed into the case.
            var target = viewModel.AvailableActions.First(action => action.Key == "input.mouseScroll");
            foreach (var term in new[] { "mouseScroll", target.LocalName })
            {
                viewModel.ActionSearch = term;
                Dispatcher.UIThread.RunJobs();

                var found = viewModel.ActionGroups.SelectMany(group => group.Actions).ToList();
                Assert.NotEmpty(found);
                Assert.Contains(found, card => ReferenceEquals(card.Definition, target));
                Assert.All(found, action => Assert.True(
                    action.Key.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || action.Definition.LocalName.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || action.Definition.LocalDescription.Contains(
                        term, StringComparison.OrdinalIgnoreCase)));

                // A search has already done the narrowing, so everything left is open to read.
                Assert.All(viewModel.ActionGroups.Where(group => group.HasActions),
                    group => Assert.True(group.IsOpen, group.Key));
            }

            // Something no action answers to empties the picker and says so, rather than
            // leaving the last result of the previous search behind under the box.
            viewModel.ActionSearch = "zzzz";
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(viewModel.ActionGroups);
            Assert.True(viewModel.HasNoActionMatch);
        });
    }

    [Fact]
    public void Choosing_an_action_marks_its_card_and_fills_the_fields_beside_it()
    {
        Ui.Run(() =>
        {
            var window = new AddActionWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;

            // The list opens with its groups folded, so the card for one action is only on
            // screen once something has narrowed the list to it — searching for its key does
            // that, and searching is also what opens the groups that hold the matches. Looking
            // for the card without searching would only work while a case that runs first has
            // happened to use this action, because "recently used" is kept in a static list.
            viewModel.ActionSearch = "mouseMove";
            Dispatcher.UIThread.RunJobs();

            // Clicking a card in the list is the same thing as choosing that action.
            var card = window.GetVisualDescendants().OfType<Button>()
                .First(button => button.Classes.Contains("ActionCard")
                    && button.DataContext is ActionCardViewModel { Key: "input.mouseMove" });
            card.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("input.mouseMove", viewModel.SelectedDefinition!.Key);
            Assert.Contains(viewModel.SelectedDefinition.LocalName, viewModel.SelectedActionName);

            // The card that is being built reads as chosen, and the search box and the groups stay
            // where they are: the list is one of the two halves of the dialog, not a question that
            // has been answered and folded away.
            Assert.True(((ActionCardViewModel)card.DataContext!).IsSelected);
            var search = window.GetVisualDescendants().OfType<TextBox>()
                .First(box => Equals(box.PlaceholderText, Strings.Get("Add.SearchAction")));
            Assert.True(search.IsEffectivelyVisible);
        });
    }

    [Fact]
    public void A_group_opened_by_hand_stays_open()
    {
        Ui.Run(() =>
        {
            var window = new AddActionWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            var heading = window.GetVisualDescendants().OfType<Button>()
                .First(button => button.Classes.Contains("GroupToggle")
                    && button.DataContext is ActionGroupViewModel { Key: "Input" });
            Assert.False(((ActionGroupViewModel)heading.DataContext!).IsOpen);

            heading.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            // A search rebuilds the groups, and the one the user opened is still open after it.
            Assert.True(viewModel.ActionGroups.First(group => group.Key == "Input").IsOpen);
            viewModel.ActionSearch = "mouse";
            Dispatcher.UIThread.RunJobs();
            Assert.True(viewModel.ActionGroups.First(group => group.Key == "Input").IsOpen);
        });
    }

    [Fact]
    public void An_action_that_has_just_been_used_waits_at_the_top_of_the_picker()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("vision.getPixel");
            viewModel.CloseRequested += _ => { };
            viewModel.SaveCommand.Execute(null);

            var window = new AddActionWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var picker = (AddActionViewModel)window.DataContext!;
            var recent = picker.ActionGroups.First(group => group.Key == "recent");

            // The group is there to save the trip back through the categories, so it is open
            // and the action is the first thing in it.
            Assert.True(recent.IsOpen);
            Assert.Equal("vision.getPixel", recent.Actions[0].Key);
        });
    }

    /// <summary>The parameter names on one line of the dialog.</summary>
    private static IEnumerable<string> Names(ParameterRowViewModel row)
    {
        yield return row.First.Definition.Name;
        if (row.Second is { } second)
        {
            yield return second.Definition.Name;
        }
    }

    // -------------------------------------------------------- keys and controller controls

    [Fact]
    public void A_hotkey_is_built_a_key_at_a_time_from_the_keyboard_on_screen()
    {
        Ui.Run(() =>
        {
            var window = new AddActionWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.SelectAction("input.hotkey");
            Dispatcher.UIThread.RunJobs();

            // The combination is a keyboard field button. What the keyboard hands back joins what
            // is already there, so Ctrl+Shift+S is put together one key at a time.
            var keys = viewModel.Parameters.First(parameter => parameter.Definition.Name == "keys");
            Assert.True(keys.IsKeys);
            Assert.False(keys.IsKey);

            var field = window.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(button => button.IsEffectivelyVisible
                    && button.Classes.Contains("KeyFieldButton")
                    && ReferenceEquals(keys, button.DataContext));
            Assert.NotNull(field);
            Assert.Equal(Strings.Get("Add.ChooseKeys"), field.Content);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBox>()
                    .Where(box => box.IsEffectivelyVisible),
                box => ReferenceEquals(keys, box.DataContext));

            // The sides are named apart on the drawn keyboard, so a combination can say which
            // Ctrl or Shift it means — the same names the trigger bindings use.
            keys.AddKey("左Ctrl");
            keys.AddKey("左Shift");
            keys.AddKey("S");
            Assert.Equal("左Ctrl+左Shift+S", keys.CurrentText);

            // A single key field is the one that is replaced, and it stays that way.
            viewModel.SelectAction("input.keyPress");
            Dispatcher.UIThread.RunJobs();
            var single = viewModel.Parameters.First(parameter => parameter.Definition.Name == "key");
            Assert.True(single.IsKey);
            Assert.False(single.IsKeys);
        });
    }

    [Fact]
    public void A_key_field_offers_the_virtual_keyboard_beside_it()
    {
        Ui.Run(() =>
        {
            var window = new AddActionWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.SelectAction("input.keyPress");
            Dispatcher.UIThread.RunJobs();

            // The key is chosen on the keyboard drawn on screen; there is no text box whose
            // spelling has to be remembered.
            var field = viewModel.Parameters.First(parameter => parameter.Definition.Name == "key");
            Assert.True(field.IsKey);

            var keyboardField = window.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(button => button.IsEffectivelyVisible
                    && button.Classes.Contains("KeyFieldButton")
                    && ReferenceEquals(field, button.DataContext));
            Assert.NotNull(keyboardField);
            Assert.Equal(Strings.Get("Add.ChooseKey"), keyboardField.Content);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<AutoCompleteBox>()
                    .Where(box => box.IsEffectivelyVisible),
                box => ReferenceEquals(field, box.DataContext));

            field.Text = "F5";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("F5", keyboardField.Content);

            keyboardField.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var pad = window.OwnedWindows.OfType<VirtualKeyboardWindow>().Single();
            Assert.Equal(field.Definition.LocalLabel, pad.Filling);
            pad.Close();

            // Nothing else gets it: a plain text field has no keys to point at.
            viewModel.SelectAction("input.typeText");
            Dispatcher.UIThread.RunJobs();

            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>()
                    .Where(button => button.IsEffectivelyVisible),
                button => button.Classes.Contains("KeyFieldButton")
                    && button.DataContext is StepParameterViewModel { IsKey: true });
        });
    }

    [Fact]
    public void The_controls_of_a_controller_are_named_on_the_pad_drawn_on_screen()
    {
        Ui.Run(() =>
        {
            var window = new AddActionWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.SelectAction("gamepad.button");
            Dispatcher.UIThread.RunJobs();

            var button = viewModel.Parameters.First(parameter => parameter.Definition.Name == "button");
            Assert.True(button.IsGamepadPad);
            Assert.False(viewModel.Parameters.First(parameter => parameter.Definition.Name == "buttonMode")
                .IsGamepadPad);

            var pad = window.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(item => item.IsEffectivelyVisible
                    && Equals(item.Content, Strings.Get("Add.OpenGamepad")));
            Assert.NotNull(pad);
            Assert.Same(button, pad.DataContext);

            // What the pad hands back is one of the choices this parameter has, and a control it
            // does not know is left alone rather than written into the step.
            button.Choose("y");
            Assert.Equal("y", Value(viewModel, "button"));
            button.Choose("both");
            Assert.Equal("y", Value(viewModel, "button"));

            // A choice that is not a control of a controller — the button of a mouse click — is
            // picked out of its list and carries no pad.
            viewModel.SelectAction("input.mouseClick");
            Dispatcher.UIThread.RunJobs();

            var mouse = viewModel.Parameters.First(parameter => parameter.Definition.Name == "button");
            Assert.False(mouse.IsGamepadPad);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>()
                    .Where(item => item.IsEffectivelyVisible),
                item => item.DataContext is StepParameterViewModel { IsGamepadPad: true });
        });
    }

    // ---------------------------------------------------------------- trying one field

    /// <summary>
    /// Taking a colour with the magnifier also writes the place it was read from into the step's own
    /// position: what the magnifier was aimed at is the pixel the step is about, so asking where as
    /// well as what is what saves lining the two up by hand afterwards.
    /// </summary>
    [Fact]
    public void Taking_a_colour_writes_the_place_it_was_read_from_into_the_step()
    {
        Ui.RunAsync(async () =>
        {
            var screen = new ColourScreen { Colour = new PixelColor(0x11, 0x22, 0x33) };
            var hook = new ClickingHook();
            var window = new AddActionWindow(null, ActionCatalog.Definitions, [], [])
            {
                Devices = new ChecksDevices { Screen = screen, Hook = hook },
            };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.SelectAction("vision.waitColor");
            Dispatcher.UIThread.RunJobs();

            var colour = viewModel.Parameters.First(parameter => parameter.Definition.Name == "color");
            PickColourButton(window, colour).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            // The click that takes the colour is one the magnifier hears through the shared hook,
            // and what it answers with comes back through the window's own queue.
            hook.Click(700, 500);

            var x = viewModel.Parameters.First(parameter => parameter.Definition.Name == "x");
            var y = viewModel.Parameters.First(parameter => parameter.Definition.Name == "y");
            Assert.True(await Settled(() => colour.CurrentText == "#112233"
                    && x.CurrentText == "700" && y.CurrentText == "500"),
                $"the step came back with colour {colour.CurrentText} at {x.CurrentText},{y.CurrentText}");

            window.Close();
            return true;
        });
    }

    /// <summary>The magnifier's own button, which is what opens it for the field it sits on.</summary>
    private static Button PickColourButton(Window window, StepParameterViewModel parameter)
        => window.GetVisualDescendants().OfType<Button>()
            .First(item => item.IsEffectivelyVisible
                && item.Name == "PickColourButton"
                && ReferenceEquals(item.DataContext, parameter));

    /// <summary>
    /// Lets the window's own work catch up: what the magnifier answers with goes through the
    /// dispatcher rather than coming back on the click, so a check that clicked it has to let the
    /// queue run before it reads the fields.
    /// </summary>
    private static async Task<bool> Settled(Func<bool> done)
    {
        for (var turn = 0; turn < 40; turn++)
        {
            Dispatcher.UIThread.RunJobs();
            if (done())
            {
                return true;
            }

            await Task.Delay(25);
        }

        return done();
    }

    /// <summary>
    /// A mouse a check clicks with: the magnifier hears the click through the same hook everything
    /// else uses, and the other six events are there because every hook has them.
    /// </summary>
    private sealed class ClickingHook : IInputHook
    {
        public event Action<MouseHookEvent>? MouseDown;

        public event Action<KeyHookEvent>? KeyDown
        {
            add { }
            remove { }
        }

        public event Action<KeyHookEvent>? KeyUp
        {
            add { }
            remove { }
        }

        public event Action<MouseHookEvent>? MouseUp
        {
            add { }
            remove { }
        }

        public event Action<MouseMovedHookEvent>? Moved
        {
            add { }
            remove { }
        }

        public event Action<MouseMovedRelativeHookEvent>? MovedRelative
        {
            add { }
            remove { }
        }

        public event Action<WheelHookEvent>? Wheel
        {
            add { }
            remove { }
        }

        /// <summary>A left click at a place, the way the magnifier hears one.</summary>
        public void Click(int x, int y)
            => MouseDown?.Invoke(new MouseHookEvent(HookMouseButton.Left, x, y, false));
    }

    /// <summary>
    /// A screen that answers with one colour wherever it is read, over a desk big enough for the
    /// magnifier to sit on it.
    /// </summary>
    private sealed class ColourScreen : IScreenDevice
    {
        public PixelColor Colour { get; set; }

        public ScreenLayout Desktop { get; } = new(new ScreenArea(0, 0, 1920, 1080),
            [new ScreenDisplay(new ScreenArea(0, 0, 1920, 1080), true)]);

        public ScreenPoint Cursor => new(0, 0);

        public PixelColor PixelAt(int x, int y) => Colour;

        public ImageFrame Capture(int x, int y, int width, int height)
            => new(width, height, new byte[width * height * 4]);

        public ScreenShot Capture(ScreenCaptureRequest request)
            => NullDeviceLayer.Instance.Screen.Capture(request);
    }

    [Fact]
    public void A_key_field_can_be_tried_on_the_spot()
    {
        Ui.RunAsync(async () =>
        {
            var window = new AddActionWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.SelectAction("input.keyPress");
            Dispatcher.UIThread.RunJobs();

            var key = viewModel.Parameters.First(parameter => parameter.Definition.Name == "key");
            key.Text = "F5";

            var devices = new RecordingDevices();
            window.Devices = devices;

            TestButton(window, key).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            // The step is tried off the thread that draws the window, so what it sent arrives a
            // moment after the click.
            Assert.True(await devices.WaitUntil(() => devices.Calls.Count >= 1),
                "the trial never reached the devices");
            Assert.Equal("keyPress F5 50", devices.Calls[0]);

            window.Close();
            return true;
        });
    }

    [Fact]
    public void A_controller_control_can_be_tried_on_the_spot()
    {
        Ui.RunAsync(async () =>
        {
            var window = new AddActionWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.SelectAction("gamepad.button");
            Dispatcher.UIThread.RunJobs();

            var button = viewModel.Parameters.First(parameter => parameter.Definition.Name == "button");
            button.Choose("y");

            var devices = new RecordingDevices();
            window.Devices = devices;

            TestButton(window, button).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            // A tap, then the letting go that keeps a tried button from staying down in the game.
            Assert.True(await devices.WaitUntil(() => devices.Calls.Count >= 3),
                $"the trial sent {string.Join(", ", devices.Calls)}");
            Assert.Equal("gamepadButton y True", devices.Calls[0]);
            Assert.Equal("gamepadButton y False", devices.Calls[1]);
            Assert.Equal("gamepadRelease", devices.Calls[^1]);

            window.Close();
            return true;
        });
    }

    /// <summary>
    /// A call to another macro is a form of that macro's own declarations: naming a macro is what
    /// puts its arguments and its results on the form, and naming another one replaces them. What
    /// was filled in for a macro is still there on the way back, so looking at another one does not
    /// throw away what the author had written.
    /// </summary>
    [Fact]
    public void A_calls_fields_are_the_declarations_of_the_macro_it_names()
    {
        Ui.Run(() =>
        {
            var window = CallWindow();
            var viewModel = (AddActionViewModel)window.DataContext!;

            // Nothing is asked for until a macro is named: what a call fills in is that macro's list.
            Assert.Null(Field(viewModel, "arg.次数"));
            Assert.Empty(viewModel.ReturnsRows);

            Field(viewModel, "macro")!.WriteValue("整理");
            Dispatcher.UIThread.RunJobs();

            var times = Field(viewModel, "arg.次数");
            Assert.NotNull(times);
            // The default belongs to the declaration, not to the call: an untouched field stays
            // empty so a changed default is what every call without a value reads.
            Assert.Equal(string.Empty, times!.CurrentText);
            Assert.Contains("3", times.Hint, StringComparison.Ordinal);
            Assert.False(times.Definition.Required);
            Assert.True(Field(viewModel, "arg.标签")!.Definition.Required);
            Assert.Equal("答案", Assert.Single(viewModel.ReturnsRows).Key);

            times.WriteValue("$n");

            // Another macro is another form...
            Field(viewModel, "macro")!.WriteValue("清空");
            Dispatcher.UIThread.RunJobs();
            Assert.Null(Field(viewModel, "arg.次数"));
            Assert.Empty(viewModel.ReturnsRows);

            // ...and coming back does not empty the fields that were filled in.
            Field(viewModel, "macro")!.WriteValue("整理");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("$n", Field(viewModel, "arg.次数")!.CurrentText);
        });
    }

    [Fact]
    public void A_call_saves_what_was_filled_in_and_opens_again_the_same_way()
    {
        Ui.Run(() =>
        {
            var window = CallWindow();
            var viewModel = (AddActionViewModel)window.DataContext!;

            Field(viewModel, "macro")!.WriteValue("整理");
            Dispatcher.UIThread.RunJobs();
            Field(viewModel, "arg.次数")!.WriteValue("$n");
            Field(viewModel, "arg.标签")!.WriteValue("开始");
            viewModel.ReturnsRows[0].Ticked = true;
            viewModel.ReturnsRows[0].Name = "答案";

            MacroStep? saved = null;
            viewModel.CloseRequested += step => saved = step;
            viewModel.SaveCommand.Execute(null);

            Assert.NotNull(saved);
            Assert.Equal("整理", saved!.Text("macro"));
            Assert.Equal("$n", saved.Text("arg.次数"));
            Assert.Equal("开始", saved.Text("arg.标签"));

            // An argument nobody filled in is not written at all, which is how the engine tells
            // "use the default" from "pass this value".
            Assert.DoesNotContain(saved.Parameters, parameter => parameter.Name == "arg.可省");
            var row = Assert.Single(saved.Parameters.First(parameter => parameter.Name == "returns").Rows);
            Assert.Equal("答案", row.Text("key"));
            Assert.Equal("答案", row.Text("name"));

            var reopened = new AddActionWindow(saved, ActionCatalog.Definitions, [], Macros());
            reopened.Show();
            Dispatcher.UIThread.RunJobs();

            var again = (AddActionViewModel)reopened.DataContext!;
            Assert.Equal("整理", Field(again, "macro")!.CurrentText);
            Assert.Equal("$n", Field(again, "arg.次数")!.CurrentText);
            Assert.Equal("开始", Field(again, "arg.标签")!.CurrentText);
            Assert.True(Assert.Single(again.ReturnsRows).Ticked);
        });
    }

    /// <summary>
    /// The window stays up from one step to the next, and the next step is a different step: what
    /// was written into the last call is not something this one said, so naming the same macro again
    /// starts from the macro's own defaults rather than from the step before it.
    /// </summary>
    [Fact]
    public void A_call_written_next_does_not_inherit_what_the_last_one_was_filled_with()
    {
        Ui.Run(() =>
        {
            var window = CallWindow();
            var viewModel = (AddActionViewModel)window.DataContext!;

            Field(viewModel, "macro")!.WriteValue("整理");
            Dispatcher.UIThread.RunJobs();
            Field(viewModel, "arg.次数")!.WriteValue("$n");

            window.MintStepId = () => "eeee";
            window.BeginStep("control.runMacro");
            Dispatcher.UIThread.RunJobs();

            Field(viewModel, "macro")!.WriteValue("整理");
            Dispatcher.UIThread.RunJobs();

            var next = Field(viewModel, "arg.次数")!;
            Assert.Equal(string.Empty, next.CurrentText);
            Assert.Contains("3", next.Hint, StringComparison.Ordinal);
        });
    }

    /// <summary>The dialog open on a call, with the project's macros to point it at.</summary>
    private static AddActionWindow CallWindow()
    {
        var window = new AddActionWindow(null, ActionCatalog.Definitions, [], Macros());
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var viewModel = (AddActionViewModel)window.DataContext!;
        viewModel.SelectAction("control.runMacro");
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    /// <summary>The macros a call can be pointed at, with what each of them declares.</summary>
    private static IReadOnlyList<MacroCallable> Macros() =>
    [
        new MacroCallable("整理",
            [
                new MacroParameter
                {
                    Name = "次数",
                    Default = "3",
                    Required = false,
                    Description = "要做多少遍。",
                },
                new MacroParameter { Name = "标签", Description = "写在每一项前面的文字。" },
                new MacroParameter { Name = "可省", Required = false },
            ],
            [new MacroResult
            {
                Name = "答案",
                Description = "整理完留下的数。",
                Type = ActionReturnType.Number,
            }]),
        new MacroCallable("清空", [], []),
    ];

    /// <summary>One field of the open form, or null when this action has no such field.</summary>
    private static StepParameterViewModel? Field(AddActionViewModel viewModel, string name)
        => viewModel.Parameters.FirstOrDefault(parameter => parameter.Definition.Name == name);

    /// <summary>The "test" button that belongs to one parameter, the way the user finds it.</summary>
    private static Button TestButton(Window window, StepParameterViewModel parameter)
        => window.GetVisualDescendants().OfType<Button>()
            .First(item => item.IsEffectivelyVisible
                && Equals(item.Content, Strings.Get("Add.TestField"))
                && ReferenceEquals(item.DataContext, parameter));

    /// <summary>
    /// A device layer with a notebook behind it: what a trial sent is written down, and everything
    /// else is refused the way a machine with no devices would refuse it. It is how the test button
    /// can be checked without a real keyboard, controller or screen being touched.
    /// </summary>
    private sealed class RecordingDevices : IDeviceLayer, IInputDevice, IGamepadDevice
    {
        public RecordingDevices() => Inputs = new SingleInputRouter(this);

        public List<string> Calls { get; } = [];

        /// <summary>Nothing watches this machine but the trial itself: the hook and the word about
        /// windows changing answer the way a machine with nothing to say does.</summary>
        public IInputHook Hook => NullDeviceLayer.Instance.Hook;

        public IInputRecorder CreateRecorder() => NullDeviceLayer.Instance.CreateRecorder();

        public IWindowEvents WindowEvents { get; } = new NoWindowEvents();

        public IGamepadReading GamepadReading { get; } = new NoGamepadReading();

        public IDriverInput Driver => NullDeviceLayer.Instance.Driver;

        public IInputRouter Inputs { get; }

        public IInputDevice Input => this;

        public IGamepadDevice Gamepad => this;

        /// <summary>Automatic, like a machine nobody has set up to read the screen another way.</summary>
        public CaptureChoice Ways { get; } = new();

        ICaptureChoice IDeviceLayer.Capture => Ways;

        public IScreenDevice Screen => NullDeviceLayer.Instance.Screen;

        public IVisionDevice Vision => NullDeviceLayer.Instance.Vision;

        public IDetectorDevice Detector => NullDeviceLayer.Instance.Detector;

        public IOcrDevice Ocr => NullDeviceLayer.Instance.Ocr;

        public IUiDevice Ui => NullDeviceLayer.Instance.Ui;

        public IFileDevice Files => NullDeviceLayer.Instance.Files;

        public IClipboardDevice Clipboard => NullDeviceLayer.Instance.Clipboard;

        public IProcessDevice Processes => NullDeviceLayer.Instance.Processes;


        /// <summary>Scripts are not part of what these checks hand in.</summary>

        public IScriptDevice Scripts => NullDeviceLayer.Instance.Scripts;

        public ISystemDevice System => NullDeviceLayer.Instance.System;

        public IWindowDevice Windows => NullDeviceLayer.Instance.Windows;

        public IBrowserDevice Browser => NullDeviceLayer.Instance.Browser;

        public ScreenPoint Cursor => new(0, 0);

        public void KeyPress(string key, int holdMs) => Calls.Add($"keyPress {key} {holdMs}");

        public void KeyDown(string key) => Calls.Add($"keyDown {key}");

        public void KeyUp(string key) => Calls.Add($"keyUp {key}");

        public void Hotkey(IReadOnlyList<string> keys, int holdMs) => Calls.Add("hotkey");

        public void TypeText(string text, int intervalMs) => Calls.Add("typeText");

        public void MoveMouse(int x, int y, int durationMs) => Calls.Add("moveMouse");

        public void MoveMouseAlong(IReadOnlyList<ScreenPoint> path, int durationMs)
            => Calls.Add("moveMouseAlong");

        public void MoveMouseRelative(int dx, int dy, int durationMs)
            => Calls.Add("moveMouseRelative");

        public void MouseDown(string button, int x, int y) => Calls.Add("mouseDown");

        public void MouseUp(string button, int x, int y) => Calls.Add("mouseUp");

        public void Click(string button, int x, int y, int clicks, int intervalMs) => Calls.Add("click");

        public void Scroll(string direction, int delta, int x, int y) => Calls.Add("scroll");

        public void Drag(string button, int startX, int startY, int endX, int endY, int durationMs,
            int steps) => Calls.Add("drag");

        public void DragAlong(string button, IReadOnlyList<ScreenPoint> path, int durationMs)
            => Calls.Add("dragAlong");

        public void Connect() => Calls.Add("gamepadConnect");

        public void Button(string button, bool down) => Calls.Add($"gamepadButton {button} {down}");

        public void Stick(string stick, int x, int y) => Calls.Add($"gamepadStick {stick} {x} {y}");

        public void Trigger(string trigger, int amount)
            => Calls.Add($"gamepadTrigger {trigger} {amount}");

        public void ReleaseAll() => Calls.Add("gamepadRelease");

        /// <summary>Waits for something to have been sent, so a check can see a click's work.</summary>
        public async Task<bool> WaitUntil(Func<bool> done)
        {
            // The trial runs off this thread, and a machine busy with the rest of the checks can
            // take a while to get to it: how long that takes says nothing about whether the click
            // worked, so there is room here rather than a queue that turns into a failure.
            var step = TimeSpan.FromMilliseconds(25);
            for (var waited = TimeSpan.Zero; waited < TimeSpan.FromSeconds(30); waited += step)
            {
                if (done())
                {
                    return true;
                }

                await Task.Delay(step);
            }

            return done();
        }
    }
}
