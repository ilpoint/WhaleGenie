using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
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
            region.AddRegion(new RegionRowViewModel { OrVariable = "shot" });

            MacroStep? saved = null;
            viewModel.CloseRequested += step => saved = step;
            viewModel.SaveCommand.Execute(null);

            var rows = Assert.Single(new[] { saved! }.ToExecutable()).Rows("region");
            Assert.Equal(2, rows.Count);
            Assert.Equal("10", Cells.Spoken(rows[0], "x"));
            Assert.Equal("20", Cells.Spoken(rows[0], "y"));
            Assert.Equal("30", Cells.Spoken(rows[0], "width"));
            Assert.Equal("40", Cells.Spoken(rows[0], "height"));
            Assert.Equal("shot", Cells.Spoken(rows[1], "text"));
        });
    }

    /// <summary>
    /// 一行「在哪里看」与四格框量的是同一套坐标：识别方式选窗口时，框选写进行里的是从目标窗口
    /// 客户区左上角量起的数。引擎按客户区读这一行，写进屏幕坐标就整块偏出窗口原点那么多。
    /// </summary>
    [Fact]
    public void A_row_picked_on_the_screen_is_counted_from_the_window_it_is_anchored_to()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("ocr.findText");
            viewModel.Windows = WindowsAnchoredAt(255, 123);
            Anchor(viewModel, "window");

            // The picker limits the drag to this client area, which is the same corner the engine
            // will read the row's four numbers from.
            Assert.True(viewModel.CoordinateWindowBounds() is (255, 123, 800, 600));

            var region = Region(viewModel);
            region.AddRegion();
            var row = Assert.Single(region.Regions);

            // 框选量到的是屏幕像素：从客户区左上角拖出一块 200×100。
            viewModel.ApplyRegionToRow(row, 255, 123, 200, 100);

            Assert.Equal("0", row.X.Text);
            Assert.Equal("0", row.Y.Text);
            Assert.Equal("200", row.Width.Text);
            Assert.Equal("100", row.Height.Text);
        });
    }

    /// <summary>一律按屏幕像素算的步骤不加也不减，行里就是框选量到的那个屏幕位置。</summary>
    [Fact]
    public void A_row_picked_on_the_screen_keeps_screen_pixels_without_a_window()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("ocr.findText");
            var region = Region(viewModel);
            region.AddRegion();
            var row = Assert.Single(region.Regions);

            viewModel.ApplyRegionToRow(row, 255, 123, 200, 100);

            Assert.Equal("255", row.X.Text);
            Assert.Equal("123", row.Y.Text);
        });
    }

    /// <summary>一行的框选走这条取数路径，而不是把拾取器的屏幕像素直接塞进行里。</summary>
    [Fact]
    public void The_row_picker_writes_the_rectangle_through_the_steps_coordinate_basis()
    {
        var source = ViewSource.All("ActionEditorView.axaml.cs");

        Assert.Contains("viewModel.ApplyRegionToRow(row, picked.X", source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("row.Put(picked.X", source, StringComparison.Ordinal);
    }

    /// <summary>A dotted window whose client area starts at the place a picker measures from.</summary>
    private static WindowsForChecks WindowsAnchoredAt(int x, int y)
        => new WindowsForChecks()
            .Add(7, "Notepad - notes.txt", "notepad", "Notepad")
            .Place(7, x - 5, y - 3, 800, 600)
            .ClientAt(7, x, y);

    /// <summary>Turns the step's 识别方式 onto a window and names the one that is open.</summary>
    private static void Anchor(AddActionViewModel viewModel, string mode)
    {
        var choice = viewModel.Parameters
            .First(parameter => parameter.Definition.Name == "anchorMode");
        choice.Option = choice.Choices.First(option => option.Value == mode);

        if (mode == "window")
        {
            viewModel.Parameters.First(parameter => parameter.Definition.Name == "window")
                .ApplyWindowRule(new WindowRule(Title: "Notepad"));
        }
    }

    /// <summary>
    /// The four numbers of a place to look are ordinary fields: each may be written as an expression
    /// the engine works out while the macro runs, and what is written is what reaches the file.
    /// </summary>
    [Fact]
    public void A_number_of_a_place_to_look_may_be_worked_out()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("vision.findImage");
            var region = Region(viewModel);

            region.AddRegion();
            var row = region.Regions[0];

            row.X.Write(Formula.Name("left"));
            row.Y.SetNumber(20);
            row.Width.SetNumber(30);
            row.Height.SetNumber(40);

            // A number written as a formula is no longer a number, and the row says so.
            Assert.True(row.HasFormula);
            Assert.False(row.X.IsPlain);

            MacroStep? saved = null;
            viewModel.CloseRequested += step => saved = step;
            viewModel.SaveCommand.Execute(null);

            var rows = Assert.Single(new[] { saved! }.ToExecutable()).Rows("region");
            var written = Assert.Single(rows);
            Assert.Equal("left", Cells.Spoken(written, "x"));
            Assert.Equal("20", Cells.Spoken(written, "y"));
            Assert.Equal("30", Cells.Spoken(written, "width"));
            Assert.Equal("40", Cells.Spoken(written, "height"));
        });
    }

    /// <summary>
    /// A place is said one way at a time: naming a variable takes the four numbers out, and writing
    /// a number takes the name out, so no row carries two answers.
    /// </summary>
    [Fact]
    public void A_row_says_where_to_look_one_way_at_a_time()
    {
        Ui.Run(() =>
        {
            var row = new RegionRowViewModel();
            row.Put(10, 20, 30, 40);

            row.OrVariable = "shot";
            Assert.Equal(string.Empty, row.X.Text);
            Assert.Equal("shot", row.ToRow().Text("text"));

            row.X.SetNumber(5);
            Assert.Equal(string.Empty, row.OrVariable);
            Assert.Equal("5", row.ToRow().Text("x"));
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
            var window = new ActionEditorWindow(null, ActionCatalog.Definitions,
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

    /// <summary>
    /// A row is four number fields with nothing to switch between: every one of them has the
    /// expression builder beside it, and one box beside them names a variable instead. Which way a
    /// row says where to look is read off the row rather than chosen before typing.
    /// </summary>
    [Fact]
    public void A_row_is_four_number_fields_and_one_variable_box()
    {
        Ui.Run(() =>
        {
            var window = new ActionEditorWindow(null, ActionCatalog.Definitions,
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

            var row = Assert.Single(region.Regions);
            var cells = row.Cells;

            // Four numbers, each with the value chip beside it: a number box draws its own up and
            // down buttons on that edge, so the chip has nowhere to go inside it.
            Assert.Equal(4, window.GetVisualDescendants().OfType<FANumberBox>()
                .Count(box => box.IsEffectivelyVisible
                    && box.DataContext is NumberCellViewModel cell && cells.Contains(cell)));
            Assert.Equal(4, window.GetVisualDescendants().OfType<Button>()
                .Count(button => button.IsEffectivelyVisible && button.Classes.Contains("ValueChip")
                    && button.Tag is NumberCellViewModel cell && cells.Contains(cell)));

            // And the one box that names a variable instead, on the same row — with its chip inside.
            Assert.Contains(window.GetVisualDescendants().OfType<TextBox>(),
                box => box.IsEffectivelyVisible && ReferenceEquals(box.DataContext, row));

            // The buttons that go with the row itself: picking one on the screen and taking the row
            // away again. Naming the variable lives in the box now, so it is not one of them.
            var buttons = window.GetVisualDescendants().OfType<Button>()
                .Where(button => button.IsEffectivelyVisible
                    && ReferenceEquals(button.DataContext, row))
                .Select(button => button.Content)
                .ToList();
            Assert.Contains(Strings.Get("Add.PickRegion"), buttons);
            Assert.Contains(Strings.Get("Add.RegionRemove"), buttons);
            Assert.DoesNotContain(Strings.Get("Add.VariablePicker"), buttons);

            // A number written as an expression swaps the spinner for the expression box, the same
            // way it does in every other number field, and writing a plain number swaps it back.
            row.X.Write(Formula.Name("left"));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(3, window.GetVisualDescendants().OfType<FANumberBox>()
                .Count(box => box.IsEffectivelyVisible
                    && box.DataContext is NumberCellViewModel cell && cells.Contains(cell)));

            row.X.SetNumber(7);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(4, window.GetVisualDescendants().OfType<FANumberBox>()
                .Count(box => box.IsEffectivelyVisible
                    && box.DataContext is NumberCellViewModel cell && cells.Contains(cell)));
        });
    }

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
                                    Cells = new(StringComparer.OrdinalIgnoreCase)
                                    {
                                        ["x"] = Formula.Text("10"),
                                        ["y"] = Formula.Text("20"),
                                        ["width"] = Formula.Text("30"),
                                        ["height"] = Formula.Text("40"),
                                    },
                                },
                                StepParameterRow.Of("text", Formula.Name("shot")),
                            ],
                        },
                    ],
                });

                MacroPackage.Save(package, [macro], new ProjectTriggers(), [], "test");

                var loaded = MacroPackage.Load(package);
                var step = Assert.Single(Assert.Single(loaded.Macros).Steps);
                var rows = Assert.Single(step.Parameters).Rows;

                Assert.Equal(2, rows.Count);
                Assert.Equal("10", rows[0].Text("x"));
                Assert.Equal("40", rows[0].Text("height"));
                Assert.Equal("shot", rows[1].Text("text"));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        });
    }
}
