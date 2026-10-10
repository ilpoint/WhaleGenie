using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// Where a step looks is a list of places rather than a line of text, so what the region picker
/// dragged out has to reach the engine as rows and the macro file has to hand them back the way
/// they went in.
/// </summary>
public class SearchRegionTests
{
    private static AddActionViewModel Open(string key)
    {
        var viewModel = new AddActionViewModel(ActionCatalog.Definitions,
            VariableChoicesForChecks.Named("shot"), []);
        viewModel.SelectAction(key);
        return viewModel;
    }

    private static StepParameterViewModel Region(AddActionViewModel viewModel)
        => viewModel.Parameters.First(parameter => parameter.Definition.Name == "region");

    [Fact]
    public void What_the_region_picker_dragged_out_reaches_the_engine()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("vision.findImage");
            var region = Region(viewModel);

            region.AddRegion();
            region.Regions[0].Put(10, 20, 30, 40);
            region.AddRegion(new RegionRowViewModel { IsFromVariable = true, Named = "$shot" });

            MacroStep? saved = null;
            viewModel.CloseRequested += step => saved = step;
            viewModel.SaveCommand.Execute(null);

            var rows = Assert.Single(new[] { saved! }.ToExecutable()).Rows("region");
            Assert.Equal(2, rows.Count);
            Assert.Equal("10", rows[0]["x"]);
            Assert.Equal("20", rows[0]["y"]);
            Assert.Equal("30", rows[0]["width"]);
            Assert.Equal("40", rows[0]["height"]);
            Assert.Equal("$shot", rows[1]["text"]);
        });
    }

    /// <summary>
    /// The dialog shows a place to look as a row, and a row is added and taken away from the
    /// dialog itself rather than by writing punctuation into a field.
    /// </summary>
    [Fact]
    public void The_dialog_adds_and_removes_a_place_to_look()
    {
        Ui.Run(() =>
        {
            var window = new AddActionWindow(null, ActionCatalog.Definitions,
                VariableChoicesForChecks.Named("shot"), []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.SelectAction("vision.findImage");
            Dispatcher.UIThread.RunJobs();

            var region = Region(viewModel);
            FindButton(window, region, Strings.Get("Add.RegionAdd"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Single(region.Regions);

            // A row carries the button that takes it away again, so the list is emptied from the
            // row itself rather than from something beside the list. It is a command rather than a
            // click handler because what it does happens to the list the row is in.
            var remove = FindButton(window, null, Strings.Get("Add.RegionRemove"));
            remove.Command!.Execute(remove.CommandParameter);
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(region.Regions);
        });
    }

    private static Button FindButton(Window window, object? data, string content)
        => window.GetVisualDescendants().OfType<Button>().Single(candidate =>
            candidate.IsEffectivelyVisible
            && Equals(candidate.Content, content)
            && (data is null || Equals(candidate.DataContext, data)));

    [Fact]
    public void The_places_a_step_looks_at_survive_the_macro_package()
    {
        Ui.Run(() =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);

            try
            {
                var package = Path.Combine(folder, "regions.wgmacro");
                var macro = new MacroItem { Name = "Watches two bars" };
                macro.Steps.Add(new MacroStep
                {
                    Type = "vision.findImage",
                    Parameters =
                    [
                        new StepParameter
                        {
                            Name = "region",
                            Kind = ActionParameterKind.Region,
                            Rows =
                            [
                                new StepParameterRow
                                {
                                    Columns = new(StringComparer.OrdinalIgnoreCase)
                                    {
                                        ["x"] = "10",
                                        ["y"] = "20",
                                        ["width"] = "30",
                                        ["height"] = "40",
                                    },
                                },
                                StepParameterRow.Of("text", "$shot"),
                            ],
                        },
                    ],
                });

                MacroPackage.Save(package, [macro], [], "test");

                var loaded = MacroPackage.Load(package);
                var step = Assert.Single(Assert.Single(loaded.Macros).Steps);
                var rows = Assert.Single(step.Parameters).Rows;

                Assert.Equal(2, rows.Count);
                Assert.Equal("10", rows[0].Text("x"));
                Assert.Equal("40", rows[0].Text("height"));
                Assert.Equal("$shot", rows[1].Text("text"));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        });
    }
}
