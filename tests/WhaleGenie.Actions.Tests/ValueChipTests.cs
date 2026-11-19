using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Views;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// The chip that opens the value picker and lives inside the box it fills in. What matters is that
/// it is inside: a chip the template drops, or one laid over the text, is worse than the button it
/// replaced — the reader cannot tell which field it belongs to.
/// </summary>
[RestoreSharedState]
public class ValueChipTests
{
    [Fact]
    public void A_chip_inside_a_text_box_is_laid_out_inside_it()
    {
        Ui.Run(() =>
        {
            var chip = Chip();
            var box = new TextBox { Text = "abc", Width = 260 };
            box.Classes.Add("Field");
            box.Classes.Add("HasChip");
            box.InnerRightContent = chip;

            var window = Show(box);

            Assert.True(chip.Bounds.Width > 0, "the template dropped the chip");
            Assert.True(chip.Bounds.Right <= box.Bounds.Width + 0.5,
                $"the chip ends at {chip.Bounds.Right:0.#} in a {box.Bounds.Width:0.#} wide box");

            window.Close();
        });
    }

    [Fact]
    public void The_breakpoints_condition_box_carries_its_chip_inside()
    {
        Ui.Run(() =>
        {
            var window = new BreakpointWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var field = window.FindControl<TextBox>("ExpressionBox")!;
            var chip = field.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Classes.Contains("ValueChip"));

            Assert.True(chip.IsEffectivelyVisible);
            Assert.True(chip.Bounds.Width > 0, "the field dropped the chip");
            Assert.True(chip.Bounds.Right <= field.Bounds.Width + 0.5,
                $"the chip ends at {chip.Bounds.Right:0.#} in a {field.Bounds.Width:0.#} wide field");

            window.Close();
        });
    }

    /// <summary>
    /// A box several lines tall puts its chip at the top: centred, it would float in the middle of
    /// the writing, reading as another paragraph rather than as the box's own button.
    /// </summary>
    [Fact]
    public void A_multi_line_box_holds_its_chip_at_the_top()
    {
        var markup = ViewSource.MarkupAnd("ActionEditorView.axaml", "ParameterScalarView.axaml",
            "ParameterStructuredView.axaml", "ParameterTailView.axaml");

        var start = markup.IndexOf("IsVisible=\"{Binding IsMultiline}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "the dialog stopped editing a stretch of text in a box of its own");

        var block = markup[start..markup.IndexOf("</TextBox>", start, StringComparison.Ordinal)];
        Assert.Contains("TextBox.InnerRightContent", block, StringComparison.Ordinal);
        Assert.Contains("Classes=\"ValueChip\"", block, StringComparison.Ordinal);
        Assert.Contains("VerticalAlignment=\"Top\"", block, StringComparison.Ordinal);
    }

    private static Button Chip() => new() { Content = "取值", Classes = { "ValueChip" } };

    private static Window Show(Control content)
    {
        var window = new Window { Width = 320, Height = 140, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }
}
