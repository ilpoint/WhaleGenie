using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using WhaleGenie.Models;
using WhaleGenie.Views;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// The window that chooses what an export holds. Building it is the part that can fail on its own:
/// the rows are made in code, so a mistake there is a blank window rather than a compile error.
/// </summary>
public class MacroChooseWindowTests
{
    [Fact]
    public void The_window_lists_every_macro_with_the_submacros_inside_it()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "整理" };
            macro.SubMacros.Add(new MacroItem { Name = "读表" });
            var other = new MacroItem { Name = "收尾" };

            var window = new MacroChooseWindow(MacroChooseKind.Export, [macro, other],
                new ProjectTriggers(), [], [macro], null);
            try
            {
                var boxes = Boxes(window).ToList();

                // The "all" tick, then one row per macro and per submacro.
                Assert.Equal(4, boxes.Count);
                Assert.Contains(boxes, box => Text(box).StartsWith("整理", System.StringComparison.Ordinal));
                Assert.Contains(boxes, box => Text(box).StartsWith("读表", System.StringComparison.Ordinal));
                Assert.Contains(boxes, box => Text(box).StartsWith("收尾", System.StringComparison.Ordinal));

                // The macro was ticked from the card it was opened on, so its submacro is covered.
                var sub = boxes.First(box => Text(box).StartsWith("读表", System.StringComparison.Ordinal));
                Assert.True(sub.IsChecked);
                Assert.False(sub.IsEnabled);
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>
    /// The same window reads a file: the rows are the file's macros, and the line underneath says
    /// where the ticked ones will land.
    /// </summary>
    [Fact]
    public void The_import_side_lists_what_the_file_holds()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "整理" };
            macro.SubMacros.Add(new MacroItem { Name = "读表" });

            var window = new MacroChooseWindow(MacroChooseKind.Import, [macro],
                new ProjectTriggers(), [], null, "from.wgmacro");
            try
            {
                var boxes = Boxes(window).ToList();
                Assert.Equal(3, boxes.Count);
                Assert.All(boxes.Skip(1), box => Assert.NotEqual(true, box.IsChecked));
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static IEnumerable<CheckBox> Boxes(ILogical root)
    {
        foreach (var child in root.LogicalChildren)
        {
            if (child is CheckBox box)
            {
                yield return box;
            }

            foreach (var nested in Boxes(child))
            {
                yield return nested;
            }
        }
    }

    private static string Text(CheckBox box) => box.Content as string ?? string.Empty;
}
