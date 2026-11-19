using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// The returns page, the halves of one line, and the page a step opens on.
public partial class ActionPageTests
{
    /// <summary>
    /// The returns page lists exactly what the catalogue says the action can hand over, in the order
    /// it declares, and no field of the step is drawn there: what a step hands over is a list of
    /// results, not one more setting about how it runs.
    /// </summary>
    [Fact]
    public void The_returns_page_lists_what_the_action_can_hand_over()
    {
        var wrong = new List<string>();

        Ui.Run(() =>
        {
            foreach (var action in ActionCatalog.RunnableActions)
            {
                var viewModel = Open(action.Key);
                var drawn = viewModel.ReturnsRows.Select(row => row.Key).ToList();
                // 引擎照旧算、老宏照旧取，只是「怎么取到画面的」这类结果不再列出来。
                var declared = action.Returns.Where(result => !result.Hidden)
                    .Select(result => result.Key).ToList();

                if (!drawn.SequenceEqual(declared, StringComparer.Ordinal))
                {
                    wrong.Add($"{action.Key}: {string.Join(",", drawn)} for {string.Join(",", declared)}");
                }

                var elsewhere = viewModel.Rows.Concat(viewModel.AdvancedRows)
                    .SelectMany(Names)
                    .Where(name => name == "returns")
                    .ToList();
                wrong.AddRange(elsewhere.Select(name => $"{action.Key}: {name} is drawn among the fields"));
            }
        });

        Assert.Empty(wrong);
    }

    /// <summary>
    /// The two halves of one line are on the same page, because they are one line: split across two
    /// pages, "where" would be asked twice and answered in two places.
    /// </summary>
    [Fact]
    public void The_halves_of_one_line_are_on_the_same_page()
    {
        var pairs = new[]
        {
            ("x", "y"), ("offsetX", "offsetY"), ("width", "height"), ("deltaX", "deltaY"),
        };

        var split = new List<string>();

        foreach (var action in Ui.Run(() => ActionCatalog.RunnableActions.ToList()))
        {
            var pages = new[]
            {
                (Name: "base", Fields: On(action, viewModel => viewModel.Rows)),
                (Name: "advanced", Fields: On(action, viewModel => viewModel.AdvancedRows)),
            };

            foreach (var (first, second) in pairs)
            {
                var left = pages.FirstOrDefault(page => page.Fields.Contains(first));
                var right = pages.FirstOrDefault(page => page.Fields.Contains(second));
                if (left.Name is not null && right.Name is not null && left.Name != right.Name)
                {
                    split.Add($"{action.Key}: {first} on {left.Name}, {second} on {right.Name}");
                }
            }
        }

        Assert.Empty(split);
    }
}
