using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
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
    private static IReadOnlyList<Button> Spinners(FANumberBox box)
        => [.. box.GetVisualDescendants().OfType<Button>().Where(button =>
            button.Name is "UpSpinButton" or "DownSpinButton")];

    [Fact]
    public void A_number_box_keeps_its_spin_buttons_out_of_the_text_row()
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
                // The buttons belong to a popup that opens when the field is focused. In a row of
                // four numbers they used to sit inside the field itself, squeezed to 16×15 by hand
                // because the theme's buttons did not fit — this is the theme saying it for us.
                Assert.Equal(FANumberBoxSpinButtonPlacementMode.Compact, box.SpinButtonPlacementMode);
                Assert.All(Spinners(box), spinner => Assert.False(spinner.IsEffectivelyVisible));
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
            var box = window.GetVisualDescendants().OfType<FANumberBox>()
                .First(one => ReferenceEquals(one.DataContext, wide));
            box.Value = double.NaN;
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
            delay.GetVisualDescendants().OfType<FANumberBox>()
                .First(one => ReferenceEquals(one.DataContext, ms)).Value = double.NaN;
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
            var box = window.GetVisualDescendants().OfType<FANumberBox>()
                .First(one => ReferenceEquals(one.DataContext, x));

            Assert.Equal(-100000d, box.Minimum);
            box.Value = -40d;
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

            // Nothing has been said about where to look, so nothing is what the four boxes show:
            // a box that displayed its lower bound would look like an answer the step had written
            // down. The four stand on one line now, so they are found as the boxes on the screen.
            var boxes = Boxes(window);
            Assert.Equal(4, boxes.Count);
            for (var at = 0; at < boxes.Count; at++)
            {
                Assert.True(double.IsNaN(boxes[at].Value));
                Assert.True(string.IsNullOrWhiteSpace(boxes[at].Text),
                    $"box {at} shows \"{boxes[at].Text}\"");
            }
        });
    }

    /// <summary>
    /// 一个地方就是一句话：左上角在哪、有多宽多高，四个数字并排一行。排成两行两列时，
    /// 「宽」掉到「X」下面，读起来像两件事，而它只是同一个矩形接着说的话。
    /// </summary>
    [Fact]
    public void A_search_region_says_its_rectangle_on_one_line()
    {
        Ui.Run(() =>
        {
            var window = Open("vision.findImage");
            Parameter(window, "region").AddRegion();
            Dispatcher.UIThread.RunJobs();

            var row = Parameter(window, "region").Regions[0];

            Point Place(NumberCellViewModel cell)
            {
                var box = window.GetVisualDescendants().OfType<FANumberBox>()
                    .First(b => ReferenceEquals(b.DataContext, cell) && b.IsEffectivelyVisible);
                return box.TranslatePoint(default, window)!.Value;
            }

            var x = Place(row.X);
            var y = Place(row.Y);
            var width = Place(row.Width);
            var height = Place(row.Height);

            // One line, read left to right: where the corner is, then how far it reaches.
            Assert.Equal(x.Y, y.Y, 1);
            Assert.Equal(y.Y, width.Y, 1);
            Assert.Equal(width.Y, height.Y, 1);
            Assert.True(y.X > x.X, "Y is not beside X");
            Assert.True(width.X > y.X, "width is not beside Y");
            Assert.True(height.X > width.X, "height is not beside width");
        });
    }

    [Theory]
    [InlineData("vision.capture")]
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

            // 四个数压成一行之后，格子里的数字是这一行的第一格：按"屏幕上看得到的第一个数字框"
            // 找它，而不是按参数对象——那一格里装的是这一格的数，不是整个参数。
            var x = Boxes(window)[0];

            var above = button.TranslatePoint(default, window)!.Value;
            var below = x.TranslatePoint(default, window)!.Value;

            Assert.True(above.Y < below.Y, "the region button does not stand above the numbers");
        });
    }

    [Fact]
    public void A_row_of_a_key_run_keeps_its_parts_apart()
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
        var row = window.GetVisualDescendants().OfType<StackPanel>()
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

    private static ActionEditorWindow Open(string key)
    {
        var window = new ActionEditorWindow(null, ActionCatalog.Definitions,
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
    private static List<FANumberBox> Boxes(Window window)
        => [.. window.GetVisualDescendants().OfType<FANumberBox>()
            .Where(box => box.IsEffectivelyVisible && box.Bounds.Width > 0)];
}
