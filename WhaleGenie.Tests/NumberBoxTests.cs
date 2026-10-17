using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The number boxes, and the rows they are laid out in. A row of several numbers with their labels
/// is the widest thing the add-action page holds, and the window may be as narrow as 900, so what
/// is checked here is that the spin buttons stay small and that a row wraps rather than running
/// under the buttons beside it.
/// </summary>
public class NumberBoxTests
{
    /// <summary>The two buttons a number box puts beside its digits, by the names the theme uses.</summary>
    private static IReadOnlyList<Button> Spinners(NumericUpDown box)
        => [.. box.GetVisualDescendants().OfType<Button>().Where(button =>
            button.Name is "PART_IncreaseButton" or "PART_DecreaseButton")];

    [Fact]
    public void The_spin_buttons_of_a_number_box_stay_small()
    {
        Ui.Run(() =>
        {
            var window = Open("vision.findImage");
            var parameter = Parameter(window, "region");
            parameter.AddRegion();
            Dispatcher.UIThread.RunJobs();

            var boxes = Boxes(window);
            Assert.NotEmpty(boxes);

            foreach (var box in boxes)
            {
                var spinners = Spinners(box);
                Assert.Equal(2, spinners.Count);
                Assert.All(spinners, spinner =>
                {
                    // The buttons sit in a column at the right and are read as a pair; wide ones take
                    // the room the digits need, and in a row of four numbers they crowd what is beside
                    // them out of the page.
                    Assert.True(spinner.Bounds.Width <= 18,
                        $"the spin button is {spinner.Bounds.Width} wide");
                    Assert.True(spinner.Bounds.Height <= 18,
                        $"the spin button is {spinner.Bounds.Height} tall");
                });
            }
        });
    }

    [Fact]
    public void A_row_of_a_search_region_wraps_instead_of_overlapping()
    {
        Ui.Run(() =>
        {
            var window = Open("vision.findImage");
            Parameter(window, "region").AddRegion();
            Dispatcher.UIThread.RunJobs();

            foreach (var width in new[] { 1024d, 900d })
            {
                window.Width = width;
                Dispatcher.UIThread.RunJobs();
                AssertNoOverlap(window, typeof(RegionRowViewModel), width);
            }
        });
    }

    [Fact]
    public void Clearing_a_number_box_leaves_the_step_having_said_nothing()
    {
        Ui.Run(() =>
        {
            var window = Open("window.move");
            var wide = Parameter(window, "width");
            var box = window.GetVisualDescendants().OfType<NumericUpDown>()
                .First(one => ReferenceEquals(one.DataContext, wide));
            box.Value = null;
            Dispatcher.UIThread.RunJobs();

            // A window side is answered by being left empty: the window keeps what it has there,
            // so the step is still whole and the side is not written down as a zero.
            Assert.Equal(string.Empty, wide.CurrentText);
            Assert.True(((AddActionViewModel)window.DataContext!).CanSave);
            Assert.False(wide.IsIncluded);

            // A length of time is a number like any other: a box with nothing in it says nothing,
            // so a step whose clock is required cannot be saved until it is answered. What a step
            // does when a length is left empty is the step's own business — a look interval left
            // empty follows the screen's refresh rate, which is why it is not written down as a 0.
            var delay = Open("control.delay");
            var ms = Parameter(delay, "ms");
            delay.GetVisualDescendants().OfType<NumericUpDown>()
                .First(one => ReferenceEquals(one.DataContext, ms)).Value = null;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(string.Empty, ms.CurrentText);
            Assert.False(((AddActionViewModel)delay.DataContext!).CanSave);
        });
    }

    /// <summary>
    /// A place on a display left of the primary one is a negative number, and the box takes it as
    /// written: a spinner whose floor is zero turns "left of here" into "the left edge here".
    /// </summary>
    [Fact]
    public void A_place_box_takes_a_negative_number()
    {
        Ui.Run(() =>
        {
            var window = Open("input.mouseMove");
            var x = Parameter(window, "x");
            var box = window.GetVisualDescendants().OfType<NumericUpDown>()
                .First(one => ReferenceEquals(one.DataContext, x));

            Assert.Equal(-100000m, box.Minimum);
            box.Value = -40m;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("-40", x.CurrentText);
        });
    }

    [Fact]
    public void An_empty_number_box_shows_nothing_rather_than_the_smallest_number()
    {
        Ui.Run(() =>
        {
            var window = Open("vision.capture");
            Dispatcher.UIThread.RunJobs();

            // Nothing has been said about where to look, so nothing is what the field shows: a box
            // that displayed its lower bound would look like an answer the step had written down.
            foreach (var name in new[] { "x", "y", "width", "height" })
            {
                var box = window.GetVisualDescendants().OfType<NumericUpDown>()
                    .First(one => one.IsEffectivelyVisible
                        && ReferenceEquals(one.DataContext, Parameter(window, name)));

                Assert.Null(box.Value);
                Assert.True(string.IsNullOrWhiteSpace(box.Text), $"{name} shows \"{box.Text}\"");
            }
        });
    }

    [Fact]
    public void A_search_region_says_its_rectangle_as_two_lines_of_two()
    {
        Ui.Run(() =>
        {
            var window = Open("vision.findImage");
            Parameter(window, "region").AddRegion();
            Dispatcher.UIThread.RunJobs();

            var row = Parameter(window, "region").Regions[0];

            Point Place(NumberCellViewModel cell)
            {
                var box = window.GetVisualDescendants().OfType<NumericUpDown>()
                    .First(b => ReferenceEquals(b.DataContext, cell) && b.IsEffectivelyVisible);
                return box.TranslatePoint(default, window)!.Value;
            }

            var x = Place(row.X);
            var y = Place(row.Y);
            var width = Place(row.Width);
            var height = Place(row.Height);

            // Where the corner is and how far the rectangle reaches are two things, each said by a
            // pair of numbers side by side.
            Assert.True(y.X > x.X, "Y is not beside X");
            Assert.Equal(x.Y, y.Y, 1);
            Assert.True(height.X > width.X, "height is not beside width");
            Assert.Equal(width.Y, height.Y, 1);
            Assert.True(width.Y > x.Y, "width is not on the line below X");
        });
    }

    [Theory]
    [InlineData("vision.capture")]
    [InlineData("ocr.recognize")]
    public void The_button_that_drags_a_whole_rectangle_stands_above_its_numbers(string key)
    {
        Ui.Run(() =>
        {
            var window = Open(key);
            Dispatcher.UIThread.RunJobs();

            // One button fills all four numbers, so it belongs above the pair of them rather than
            // beside one and looking like an answer to that one.
            var button = window.GetVisualDescendants().OfType<Button>()
                .First(pressed => pressed.IsEffectivelyVisible
                    && pressed.Content as string == Strings.Get("Add.PickRegion"));
            var x = window.GetVisualDescendants().OfType<NumericUpDown>()
                .First(box => box.IsEffectivelyVisible
                    && ReferenceEquals(box.DataContext, Parameter(window, "x")));

            var above = button.TranslatePoint(default, window)!.Value;
            var below = x.TranslatePoint(default, window)!.Value;

            Assert.True(above.Y < below.Y, "the region button does not stand above the numbers");
        });
    }

    [Fact]
    public void A_row_of_a_key_run_wraps_instead_of_overlapping()
    {
        Ui.Run(() =>
        {
            var window = Open("input.keySequence");
            Parameter(window, "keySequence").AddKeyRow();
            Dispatcher.UIThread.RunJobs();

            foreach (var width in new[] { 1024d, 900d })
            {
                window.Width = width;
                Dispatcher.UIThread.RunJobs();
                AssertNoOverlap(window, typeof(KeyRowViewModel), width);
            }
        });
    }

    /// <summary>
    /// Everything in one row of the list, as the rectangles they were given. Two of them sharing
    /// ground is what a user sees as a field covered by the button beside it.
    /// </summary>
    private static void AssertNoOverlap(Window window, System.Type rowType, double width)
    {
        var row = window.GetVisualDescendants().OfType<WrapPanel>()
            .FirstOrDefault(panel => panel.DataContext?.GetType() == rowType);
        Assert.NotNull(row);

        var parts = row!.Children.OfType<Control>()
            .Where(child => child.IsEffectivelyVisible)
            .Select(child => (Child: child, Corner: child.TranslatePoint(default, window)))
            .Where(item => item.Corner is not null)
            .Select(item => new Rect(item.Corner!.Value, item.Child.Bounds.Size))
            .ToList();

        Assert.True(parts.Count >= 2, "the row did not lay its parts out");

        for (var i = 0; i < parts.Count; i++)
        {
            for (var j = i + 1; j < parts.Count; j++)
            {
                var shared = parts[i].Intersect(parts[j]);
                Assert.True(shared.Width <= 0.5 || shared.Height <= 0.5,
                    $"at {width} wide two parts of a row overlap over {shared}");
            }
        }
    }

    private static AddActionWindow Open(string key)
    {
        var window = new AddActionWindow(null, ActionCatalog.Definitions,
            VariableChoicesForChecks.Named("shot"), []);

        window.Show();
        Dispatcher.UIThread.RunJobs();

        ((AddActionViewModel)window.DataContext!).SelectAction(key);
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static StepParameterViewModel Parameter(Window window, string name)
        => ((AddActionViewModel)window.DataContext!).Parameters
            .First(parameter => parameter.Definition.Name == name);

    /// <summary>The number boxes that are on screen, which is what the user is looking at.</summary>
    private static List<NumericUpDown> Boxes(Window window)
        => [.. window.GetVisualDescendants().OfType<NumericUpDown>()
            .Where(box => box.IsEffectivelyVisible && box.Bounds.Width > 0)];
}
