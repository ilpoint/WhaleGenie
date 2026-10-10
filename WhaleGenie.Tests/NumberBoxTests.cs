using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
    public void A_row_of_a_key_run_wraps_instead_of_overlapping()
    {
        Ui.Run(() =>
        {
            var window = Open("input.keySequence");
            Parameter(window, "keys").AddKeyRow();
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
