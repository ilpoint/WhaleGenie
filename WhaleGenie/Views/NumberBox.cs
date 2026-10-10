using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace WhaleGenie.Views;

/// <summary>
/// A number box whose two spin buttons are small and stacked. The theme gives each of them a square
/// as tall as the box and 34 pixels wide, and lays the two of them side by side, so the 88-pixel box
/// a search region uses — and the 84-pixel one a key run's hold and gap use — spends most of its
/// width on buttons and leaves the digits a sliver, with the label beside it crowded out.
/// </summary>
/// <remarks>
/// The size is written onto the buttons rather than set from a style because the theme writes it
/// inside its own template, which a style cannot reach past; the arrangement is flipped to a column
/// for the same reason. What is left alone is everything else about the box: the digits, the step
/// size, the way it is typed into and the way it refuses what it cannot read.
/// </remarks>
public class NumberBox : NumericUpDown
{
    /// <summary>How wide one button of the column is, which is what leaves the digits their room.</summary>
    private const double ButtonWidth = 16;

    /// <summary>How tall one button of the column is: the box is 30 tall, so the two fill it.</summary>
    private const double ButtonHeight = 15;

    /// <summary>How tall the box itself is, which is what the fields beside it are.</summary>
    private const double BoxHeight = 30;

    public NumberBox()
    {
        MinHeight = BoxHeight;

        // The buttons are built when the box is laid out, which is after its own template has been
        // applied, so the sizing waits until the box is in the tree and its parts are there.
        Loaded += (_, _) => Narrow();
    }

    /// <summary>
    /// The look of a number box, taken from the control this one is a kind of: Avalonia picks a
    /// control's theme by its own type, and there is no theme by this name.
    /// </summary>
    protected override System.Type StyleKeyOverride => typeof(NumericUpDown);

    /// <summary>The name the theme gives the panel the two buttons sit in.</summary>
    private const string ColumnPart = "PART_SpinnerPanel";

    private const string IncreasePart = "PART_IncreaseButton";

    private const string DecreasePart = "PART_DecreaseButton";

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        Narrow();
    }

    /// <summary>
    /// Gives the two spin buttons the size and the arrangement this box wants. Nothing happens until
    /// they are there, so it is safe to ask at any point.
    /// </summary>
    private void Narrow()
    {
        if (this.GetVisualDescendants().OfType<ButtonSpinner>().FirstOrDefault() is not { } spinner)
        {
            return;
        }

        spinner.ApplyTemplate();

        // Up above down is the way a number is stepped, and a column costs the width of one button
        // where a row of two costs the width of both.
        if (Part<StackPanel>(spinner, ColumnPart) is { } column)
        {
            column.Orientation = Orientation.Vertical;
        }

        Squeeze(Part<Button>(spinner, IncreasePart));
        Squeeze(Part<Button>(spinner, DecreasePart));
    }

    /// <summary>One named part of the spinner the theme built, or null when it is not there.</summary>
    private static T? Part<T>(ButtonSpinner spinner, string name)
        where T : Control
        => spinner.GetVisualDescendants().OfType<T>().FirstOrDefault(part => part.Name == name);

    /// <summary>Sizes one button of the column, unless the template did not hand it over.</summary>
    private static void Squeeze(Button? button)
    {
        if (button is null)
        {
            return;
        }

        button.MinWidth = ButtonWidth;
        button.Width = ButtonWidth;
        button.MinHeight = 0;
        button.Height = ButtonHeight;
        button.Padding = default;
    }
}
