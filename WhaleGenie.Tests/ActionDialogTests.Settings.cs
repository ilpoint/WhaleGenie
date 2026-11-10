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

// The page a step opens on, and the settings that sit behind a fold.
public partial class ActionDialogTests
{
    [Fact]
    public void The_pages_hold_the_fields_a_step_is_read_a_part_at_a_time()
    {
        Ui.Run(() =>
        {
            var window = new ActionEditorWindow(null, ActionCatalog.Definitions, [], []);
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
            var window = new ActionEditorWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // Six fields, which is what used to push the fold below the bottom edge of the dialog
            // and leave a step with settings no one could reach.
            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.SelectAction("input.mouseClick");
            Dispatcher.UIThread.RunJobs();

            // 页签现在是库里的 `TabStrip`：只有页签条，没有内容宿主——这几个页共用同一个表单。
            var tab = window.GetVisualDescendants().OfType<TabStrip>().Single()
                .GetVisualDescendants().OfType<TabStripItem>()
                .First(item => item.IsEffectivelyVisible);

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
}
