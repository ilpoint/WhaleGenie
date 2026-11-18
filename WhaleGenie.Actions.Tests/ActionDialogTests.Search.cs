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

// The knobs of a search: what a pixel, picture or checksum step asks about.
public partial class ActionDialogTests
{
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
            // The colour of the reference picture that keeps changing, and how near a pixel has to
            // be to it, are knobs of that same kind: a step that leaves them alone wants the default.
            Assert.Contains("confidence", visible);
            Assert.Contains("matchIndex", folded);
            Assert.Contains("anchorMode", visible);
            Assert.Contains("transparentColors", folded);
            Assert.Contains("autoTransparent", folded);
            Assert.Contains("tolerance", folded);

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
}
