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

// The dialog's pages, what each one asks, and the order they are read in.
public partial class ActionDialogTests
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
            Assert.True(WindowRule.TryParse(FormulaText.Write(posted.Written), out var reached));
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
        var markup = ViewSource.MarkupAnd("ActionEditorView.axaml", "ParameterScalarView.axaml",
            "ParameterStructuredView.axaml", "ParameterTailView.axaml");

        Assert.Contains("MetaComment", Page(markup, "OnBase"), StringComparison.Ordinal);
        Assert.DoesNotContain("MetaComment", Page(markup, "OnOther"), StringComparison.Ordinal);
    }

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
        });
    }
}
