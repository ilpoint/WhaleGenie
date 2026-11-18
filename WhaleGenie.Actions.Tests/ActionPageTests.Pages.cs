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

// Which page a field belongs to, and what lives on the advanced one.
public partial class ActionPageTests
{
    /// <summary>
    /// Every field of a step is edited on one page. A field drawn in two places is a field with two
    /// answers being written to it, which is how a form comes to disagree with itself.
    /// </summary>
    [Fact]
    public void Every_field_belongs_to_one_page()
    {
        var clashes = new List<string>();

        foreach (var action in Ui.Run(() => ActionCatalog.RunnableActions.ToList()))
        {
            var basic = On(action, viewModel => viewModel.Rows);
            var advanced = On(action, viewModel => viewModel.AdvancedRows);

            var twice = basic.Concat(advanced)
                .GroupBy(name => name, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key);

            clashes.AddRange(twice.Select(name => $"{action.Key}: {name}"));
        }

        Assert.Empty(clashes);
    }

    /// <summary>
    /// What is worth tuning rather than what the step is: the way a picture is recognised, which of
    /// several hits to take, where the coordinates are counted from, how the input is sent, and the
    /// format and environment details of a file or a program. These are the answers almost every
    /// macro wants left alone, so they live behind the fold of the advanced page wherever they
    /// appear. One name is missing on purpose, because it means different things in different
    /// actions: <c>intervalMs</c> is a step's own beat in a loop and the wait between keystrokes in
    /// another, and how often the screen is read is no longer a step's field at all. <c>matchMode</c>
    /// is how strictly writing is compared in one and how rows are picked out of a file in another.
    /// Two more are the same word for two things: <c>algorithm</c> is how a picture is compared and
    /// also which hash a checksum is asked for, and <c>steps</c> is the body of a block and also how
    /// far a drag is broken up. Both are checked where they mean the tuning they are.
    /// </summary>
    private static readonly string[] Tuning =
    [
        "minFeatures", "transparentColors", "autoTransparent", "matchIndex", "orderBy", "nearX", "nearY",
        "scoreWeight",
        "style",
        "preprocess", "colorFilter", "colorTolerance", "fixText",
        "expected", "minScore", "contentPattern", "encoding", "outputEncoding", "lineEnding", "quoteAll",
        "emptyCells", "align", "separator", "depth", "sortBy", "descending", "withinCount",
        "withinUnit", "minKb", "maxKb", "relative", "columns", "matchColumn", "matchValue",
        "maxRows", "headless", "limit", "skipBlankLines", "trim", "caseSensitive",
        "multiline", "shape",
        "afterMs", "hidden", "runAsAdmin", "details", "inputSize", "iou",
    ];

    [Fact]
    public void What_is_tuned_rather_than_what_the_step_is_lives_on_the_advanced_page()
    {
        // One name is left alone for the same reason the two above are: an encoding is a format
        // detail of a file being read or written in one action, and in a script action it is the
        // second thing the step asks for after which kind of script it is, so it stands on the
        // basic page beside the kind it belongs to.
        string[] theKindItself = ["script.run.encoding"];

        var misplaced = new List<string>();

        foreach (var action in Ui.Run(() => ActionCatalog.RunnableActions.ToList()))
        {
            var basic = On(action, viewModel => viewModel.Rows);
            foreach (var name in Tuning.Where(name => basic.Contains(name, StringComparer.Ordinal)))
            {
                if (theKindItself.Contains($"{action.Key}.{name}", StringComparer.Ordinal))
                {
                    continue;
                }

                misplaced.Add($"{action.Key}: {name}");
            }
        }

        Assert.Empty(misplaced);
    }

    /// <summary>
    /// 锚点是「几个同名元素里是哪一个」的第二句话，与「第几个命中」同一类：两个一起收在高级设置里，
    /// 不占基础页的地方。十二个带选择器的界面动作都要有这三格。
    /// </summary>
    [Fact]
    public void An_element_anchor_is_asked_for_behind_the_fold()
    {
        string[] withSelector =
        [
            "uia.exists", "uia.find", "uia.waitElement", "uia.click", "uia.invoke", "uia.setText",
            "uia.read", "uia.select", "uia.check", "uia.expand", "uia.scrollIntoView",
            "uia.readTable",
        ];

        string[] anchor = ["anchorElement", "anchorSide", "anchorGap"];
        var misplaced = new List<string>();

        foreach (var key in withSelector)
        {
            var basic = On(ActionCatalog.Find(key)!, viewModel => viewModel.Rows);
            var folded = On(ActionCatalog.Find(key)!, viewModel => viewModel.AdvancedRows);

            foreach (var name in anchor)
            {
                if (basic.Contains(name, StringComparer.Ordinal))
                {
                    misplaced.Add($"{key}.{name} is on the basic page");
                }

                if (!folded.Contains(name, StringComparer.Ordinal))
                {
                    misplaced.Add($"{key}.{name} is not behind the fold");
                }
            }
        }

        Assert.Empty(misplaced);
    }
}
