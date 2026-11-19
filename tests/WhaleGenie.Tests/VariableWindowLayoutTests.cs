using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// Where a field's label sits beside its field. A label centred against the whole block — the box,
/// the hint under it and the problem line — moves down as soon as a hint wraps, and the row then
/// reads as if the label belonged to the line below the box rather than to the box.
/// </summary>
public class VariableWindowLayoutTests
{
    [Fact]
    public void The_local_variable_form_lines_each_label_up_with_its_box()
    {
        Ui.Run(() =>
        {
            var window = new LocalVariablesWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var model = (LocalVariablesViewModel)window.DataContext!;
            model.NewCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            AssertOnOneLine(window, "NameLabel", "NameBox");
            AssertOnOneLine(window, "TypeLabel", "TypeBox");
            AssertOnOneLine(window, "InitialLabel", "InitialBox");
            AssertOnOneLine(window, "DescriptionLabel", "DescriptionBox");
            AssertLabelColumn(window);

            AssertScrollbarLane(window);

            Ui.CloseAndDiscard(window);
        });
    }

    [Fact]
    public void The_global_variable_form_lines_each_label_up_with_its_box()
    {
        Ui.Run(() =>
        {
            var window = new GlobalVariablesWindow([], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var model = (GlobalVariablesViewModel)window.DataContext!;
            model.NewCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            AssertOnOneLine(window, "NameLabel", "NameBox");
            AssertOnOneLine(window, "TypeLabel", "TypeBox");
            AssertOnOneLine(window, "DefaultLabel", "DefaultValueBox");
            AssertOnOneLine(window, "ValueLabel", "CurrentValueBox");
            AssertOnOneLine(window, "DescriptionLabel", "DescriptionBox");
            AssertLabelColumn(window);

            AssertScrollbarLane(window);

            Ui.CloseAndDiscard(window);
        });
    }

    /// <summary>
    /// The manager for one macro's own values and the project's shared ones are the same window
    /// twice: a person switching between them should not have to re-find where the fields are.
    /// </summary>
    [Fact]
    public void The_two_variable_windows_share_one_size()
    {
        Ui.Run(() =>
        {
            var local = new LocalVariablesWindow();
            var global = new GlobalVariablesWindow([], []);
            local.Show();
            global.Show();
            Dispatcher.UIThread.RunJobs();

            foreach (var window in new Window[] { local, global })
            {
                Assert.Equal(780, window.Width);
                Assert.Equal(540, window.Height);
                Assert.Equal(720, window.MinWidth);
                Assert.Equal(480, window.MinHeight);
            }

            Ui.CloseAndDiscard(local);
            Ui.CloseAndDiscard(global);
        });
    }

    /// <summary>
    /// The midlines are what a word and a box can share. Comparing the tops instead would call a
    /// label aligned only when the box beside it happens to be one line tall.
    /// </summary>
    private static void AssertOnOneLine(Window window, string labelName, string fieldName)
    {
        var label = window.FindControl<TextBlock>(labelName);
        var field = window.FindControl<Control>(fieldName);
        Assert.NotNull(label);
        Assert.NotNull(field);

        var labelBox = Place(label!, window);
        var fieldBox = Place(field!, window);

        Assert.True(Math.Abs(labelBox.Center.Y - fieldBox.Center.Y) <= 1,
            $"{labelName} sits at {labelBox.Center.Y:0.0}, {fieldName} at {fieldBox.Center.Y:0.0}");

        // The word ends just before its box. A label left at the start of a wide column leaves
        // the box adrift to the right, which reads as a gap rather than as a label.
        var gap = fieldBox.X - labelBox.Right;
        Assert.InRange(gap, 8, 12);
    }


    /// <summary>
    /// The labels start on one left edge, and their column is no wider than the widest word the
    /// language has. Left alignment is what keeps the form from looking pushed to the right; the
    /// shared, measured width is what keeps the boxes on one line without a fixed gutter.
    /// </summary>
    private static void AssertLabelColumn(Window window)
    {
        var labels = FieldLabelColumn.Labels(window);
        Assert.NotEmpty(labels);

        var widest = labels.Max(FieldLabelColumn.WidthOf);
        foreach (var label in labels)
        {
            var grid = Assert.IsType<Grid>(label.Parent);
            Assert.InRange(grid.ColumnDefinitions[0].Width.Value, widest - 1, widest + 1);
        }

        var visible = labels.Where(text => text.IsEffectivelyVisible).ToList();
        var lefts = visible.Select(label => Math.Round(Place(label, window).X, 1)).Distinct().ToList();
        Assert.Single(lefts);
    }


    /// <summary>
    /// The right edge belongs to the scrollbar: the field stops before the bar, and the bar sits
    /// on the panel's edge instead of over the input's last few pixels.
    /// </summary>
    private static void AssertScrollbarLane(Window window)
    {
        var field = window.FindControl<Control>("NameBox")!;
        var scroll = field.GetVisualAncestors().OfType<ScrollViewer>().First();
        var bar = scroll.GetVisualDescendants().OfType<ScrollBar>()
            .First(candidate => candidate.Name == "PART_VerticalScrollBar"
                && candidate.Bounds.Width > 0);
        var panel = scroll.GetVisualAncestors().OfType<Border>().First();

        // The bar is only laid out when it has something to scroll; showing it here measures the
        // lane itself, which is the part the layout owns.
        scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Visible;
        Dispatcher.UIThread.RunJobs();

        var fieldBox = Place(field, window);
        var barBox = Place(bar, window);
        var panelBox = Place(panel, window);

        Assert.True(bar.IsVisible && barBox.Width > 0,
            "the fields should be scrolling at this height");
        Assert.True(barBox.X >= fieldBox.Right + 4,
            $"the scrollbar starts at {barBox.X:0.#}, the field ends at {fieldBox.Right:0.#}");
        Assert.InRange(panelBox.Right - barBox.Right, 2, 8);
    }

    private static Rect Place(Control control, Visual window)
    {
        var at = control.TranslatePoint(default, window);
        Assert.NotNull(at);
        return new Rect(at!.Value, control.Bounds.Size);
    }
}
