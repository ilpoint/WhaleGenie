using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// A step's settings are read a page at a time, and which page a field is on is a question about
/// the field: what the step is, how it does it, or the name it leaves its answer under. Those are
/// rules about the whole catalogue rather than about one action, so they are checked over it.
/// </summary>
public class ActionPageTests
{
    private static AddActionViewModel Open(string key)
    {
        var viewModel = new AddActionViewModel(ActionCatalog.Definitions, [], []);
        viewModel.SelectAction(key);
        return viewModel;
    }

    /// <summary>The names on one page of an action, which is what "a field is on one page" is about.</summary>
    private static List<string> On(ActionDefinition action, Func<AddActionViewModel,
        IEnumerable<ParameterRowViewModel>> page)
        => Ui.Run(() => page(Open(action.Key)).SelectMany(Names).ToList());

    private static IEnumerable<string> Names(ParameterRowViewModel row)
    {
        yield return row.First.Definition.Name;
        if (row.Second is { } second)
        {
            yield return second.Definition.Name;
        }
    }

    /// <summary>
    /// Every field of a step is edited on one page. A field drawn in two places is a field with two
    /// answers being written to it, which is how a form comes to disagree with itself.
    ///
    /// A rectangle is the one thing written on two pages, and it is one value with two ways of
    /// saying it rather than two fields: where it is dragged out is what the step is about and
    /// stands on the basic page, and the four numbers that make it exact are on the advanced page.
    /// Both read the same row, so they cannot disagree — and each half has to be somewhere.
    /// </summary>
    [Fact]
    public void Every_field_belongs_to_one_page()
    {
        var clashes = new List<string>();

        foreach (var action in Ui.Run(() => ActionCatalog.RunnableActions.ToList()))
        {
            var basic = On(action, viewModel => viewModel.Rows);
            var advanced = On(action, viewModel => viewModel.AdvancedRows);
            var output = On(action, viewModel => viewModel.OutputRows);

            var rectangles = Ui.Run(() => Open(action.Key).Parameters
                .Where(parameter => parameter.IsRegion)
                .Select(parameter => parameter.Definition.Name)
                .ToList());

            var drawn = basic.Concat(advanced).Concat(output)
                .Where(name => !rectangles.Contains(name, StringComparer.Ordinal))
                .ToList();
            var twice = drawn.GroupBy(name => name, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key);

            clashes.AddRange(twice.Select(name => $"{action.Key}: {name}"));

            clashes.AddRange(rectangles
                .Where(name => !(basic.Contains(name, StringComparer.Ordinal)
                    && advanced.Contains(name, StringComparer.Ordinal)))
                .Select(name => $"{action.Key}: {name} is not one half on each page"));
        }

        Assert.Empty(clashes);
    }

    /// <summary>
    /// What is worth tuning rather than what the step is: the way a picture is recognised, which of
    /// several hits to take, where the coordinates are counted from, how the input is sent, and the
    /// format and environment details of a file or a program. These are the answers almost every
    /// macro wants left alone, so they live behind the fold of the advanced page wherever they
    /// appear. Two names are missing on purpose, because they mean different things in different
    /// actions: <c>intervalMs</c> is a step's own beat in one action and how often the screen is
    /// looked at in another, and <c>matchMode</c> is how strictly writing is compared in one and how
    /// rows are picked out of a file in another. Two more are the same word for two things:
    /// <c>algorithm</c> is how a picture is compared and also which hash a checksum is asked for, and
    /// <c>steps</c> is the body of a block and also how far a drag is broken up. Both are checked
    /// where they mean the tuning they are.
    /// </summary>
    private static readonly string[] Tuning =
    [
        "minFeatures", "ignoreColor", "matchIndex", "allMatches", "orderBy",
        "anchorMode", "anchorWindow", "anchorSelector", "inputMode", "targetWindow", "style",
        "captureMode", "frameMaxAgeMs", "preprocess", "colorFilter", "colorTolerance", "fixText",
        "expected", "minScore", "language", "encoding", "outputEncoding", "lineEnding", "quoteAll",
        "emptyCells", "align", "separator", "depth", "sortBy", "descending", "withinCount",
        "withinUnit", "minKb", "maxKb", "relative", "columns", "matchColumn", "matchValue",
        "maxRows", "headless", "storeAs", "limit", "skipBlankLines", "trim", "shape", "pollMs",
        "afterMs", "hidden", "runAsAdmin",
    ];

    [Fact]
    public void What_is_tuned_rather_than_what_the_step_is_lives_on_the_advanced_page()
    {
        var misplaced = new List<string>();

        foreach (var action in Ui.Run(() => ActionCatalog.RunnableActions.ToList()))
        {
            var basic = On(action, viewModel => viewModel.Rows);
            foreach (var name in Tuning.Where(name => basic.Contains(name, StringComparer.Ordinal)))
            {
                misplaced.Add($"{action.Key}: {name}");
            }
        }

        Assert.Empty(misplaced);
    }

    /// <summary>
    /// A step that looks at the screen waits between two looks, and that number is not the same
    /// question as the beat of a step that presses a key: one is how much a loop may cost, the other
    /// is how the presses read. The first is tuning and the second is part of the action.
    /// </summary>
    [Theory]
    [InlineData("vision.findImage", "advanced")]
    [InlineData("vision.waitStable", "advanced")]
    [InlineData("ocr.findText", "advanced")]
    [InlineData("ocr.clickText", "advanced")]
    [InlineData("input.keyPress", "base")]
    [InlineData("input.mouseClick", "base")]
    [InlineData("input.mouseScroll", "base")]
    public void A_step_that_looks_at_the_screen_tunes_its_rhythm_on_the_advanced_page(
        string action, string page)
    {
        Ui.Run(() =>
        {
            var viewModel = Open(action);
            var names = page == "advanced"
                ? viewModel.AdvancedRows.SelectMany(Names).ToList()
                : viewModel.Rows.SelectMany(Names).ToList();

            Assert.Contains("intervalMs", names);
        });
    }

    /// <summary>
    /// How a picture is compared — the way of recognising it, how many feature pairs have to line
    /// up, which colour to ignore, and in what order the hits are counted — is the same kind of
    /// answer as the rest of the tuning, and is asked for on the advanced page.
    /// </summary>
    [Theory]
    [InlineData("vision.findImage")]
    [InlineData("vision.waitImage")]
    [InlineData("vision.clickImage")]
    public void How_a_picture_is_recognised_is_asked_for_on_the_advanced_page(string action)
    {
        var finder = Ui.Run(() => ActionCatalog.Find(action)!);
        var advanced = On(finder, viewModel => viewModel.AdvancedRows);

        Assert.Contains("algorithm", advanced);
        Assert.Contains("minFeatures", advanced);
        Assert.Contains("ignoreColor", advanced);
        Assert.Contains("orderBy", advanced);
    }

    /// <summary>
    /// How far a drag is broken up, and how the pointer travels, are tuning; the two ends of the
    /// drag are what the step is.
    /// </summary>
    [Fact]
    public void How_a_drag_is_broken_up_is_tuning()
    {
        var drag = Ui.Run(() => ActionCatalog.Find("input.mouseDrag")!);
        var advanced = On(drag, viewModel => viewModel.AdvancedRows);

        Assert.Contains("steps", advanced);
        Assert.Contains("style", advanced);

        // Where the drag starts and ends is what the step is, so it is on the basic page along with
        // the button it presses.
        var basic = On(drag, viewModel => viewModel.Rows);
        Assert.Contains("startX", basic);
        Assert.Contains("endX", basic);
    }

    /// <summary>
    /// A rectangle is dragged out on the basic page and made exact on the advanced page: the four
    /// numbers are what is tuned, and the picking is what the step is.
    /// </summary>
    [Theory]
    [InlineData("vision.capture")]
    [InlineData("ocr.recognize")]
    [InlineData("vision.findImage")]
    [InlineData("ocr.findText")]
    public void A_rectangle_is_picked_on_the_basic_page_and_made_exact_on_the_advanced_page(
        string action)
    {
        Ui.Run(() =>
        {
            var viewModel = Open(action);
            var basic = viewModel.Rows.SelectMany(Names).ToList();
            var advanced = viewModel.AdvancedRows.SelectMany(Names).ToList();

            Assert.Contains("region", basic);
            Assert.Contains("region", advanced);

            // The four numbers are on the advanced page and nowhere else; the basic page shows what
            // was picked rather than four boxes to type in.
            Assert.DoesNotContain("x", basic);
            Assert.DoesNotContain("width", basic);
        });
    }

    /// <summary>
    /// The three fields that say how a step reads the screen — where the picture comes from, how old
    /// a picture it will accept, and how long it leaves between two looks — belong together on the
    /// advanced page, because the answers almost every macro wants are the defaults.
    /// </summary>
    [Fact]
    public void How_a_step_reads_the_screen_is_on_the_advanced_page()
    {
        var misplaced = new List<string>();
        var about = new[] { "captureMode", "frameMaxAgeMs", "intervalMs" };

        foreach (var action in Ui.Run(() => ActionCatalog.RunnableActions.ToList()))
        {
            var basic = On(action, viewModel => viewModel.Rows);
            var advanced = On(action, viewModel => viewModel.AdvancedRows);
            var output = On(action, viewModel => viewModel.OutputRows);
            if (!basic.Concat(advanced).Concat(output).Contains("captureMode", StringComparer.Ordinal))
            {
                continue;
            }

            foreach (var name in about)
            {
                if (!advanced.Contains(name, StringComparer.Ordinal))
                {
                    misplaced.Add($"{action.Key}: {name}");
                }
            }
        }

        Assert.Empty(misplaced);
    }

    /// <summary>
    /// The step that is about one window offers only the ways a window can be read, because the ways
    /// of reading the desktop are not answers to the question it asks.
    /// </summary>
    [Fact]
    public void The_capture_window_step_offers_only_the_ways_a_window_is_read()
    {
        var offered = Ui.Run(() => ActionCatalog.Definitions
            .First(definition => definition.Key == "vision.captureWindow")
            .Parameters.First(parameter => parameter.Name == "captureMode")
            .OptionChoices.Select(choice => choice.Value).ToList());

        Assert.Equal(["auto", "printWindow", "graphicsCapture"], offered);
    }

    /// <summary>
    /// The rhythm a step that waits for the screen looks at it with is the one it had before the
    /// field existed, so a macro that says nothing about it behaves the way it always has.
    /// </summary>
    [Theory]
    [InlineData("vision.waitColor", "50")]
    [InlineData("vision.waitStable", "100")]
    [InlineData("vision.waitImage", "200")]
    [InlineData("vision.findColor", "200")]
    [InlineData("vision.clickImage", "200")]
    [InlineData("ocr.clickText", "200")]
    [InlineData("vision.findImage", "0")]
    [InlineData("condition.imageExists", "0")]
    public void A_step_that_looks_at_the_screen_keeps_the_rhythm_it_had(string action, string intervalMs)
    {
        var field = Ui.Run(() => ActionCatalog.Definitions
            .First(definition => definition.Key == action)
            .Parameters.First(parameter => parameter.Name == "intervalMs"));

        Assert.Equal(intervalMs, field.DefaultValue);
        Assert.Equal(0m, field.Minimum);
        Assert.Equal(60000m, field.Maximum);
    }

    /// <summary>
    /// Which window a step works on, and how that window is recognised, is what the step is rather
    /// than how it does it: a window named wrongly is a step that quietly does nothing, so all three
    /// parts of the name are read on the basic page.
    /// </summary>
    [Fact]
    public void How_a_window_is_recognised_is_on_the_basic_page()
    {
        var misplaced = new List<string>();

        foreach (var action in Ui.Run(() => ActionCatalog.RunnableActions.ToList()))
        {
            var basic = On(action, viewModel => viewModel.Rows);
            var advanced = On(action, viewModel => viewModel.AdvancedRows);
            if (!basic.Concat(advanced).Contains("compareBy", StringComparer.Ordinal))
            {
                continue;
            }

            misplaced.AddRange(new[] { "title", "matchBy", "compareBy" }
                .Where(name => !basic.Contains(name, StringComparer.Ordinal))
                .Select(name => $"{action.Key}: {name}"));
        }

        Assert.Empty(misplaced);
    }

    /// <summary>
    /// The page about the names a step leaves behind holds those names and nothing else: a field
    /// whose name is the answer belongs there, and a field naming a variable it reads does not —
    /// that one is part of what the step is.
    /// </summary>
    [Fact]
    public void The_output_page_holds_the_names_the_step_leaves_behind()
    {
        var wrong = new List<string>();

        foreach (var action in Ui.Run(() => ActionCatalog.RunnableActions.ToList()))
        {
            foreach (var name in On(action, viewModel => viewModel.OutputRows))
            {
                if (name != "saveTo" && !name.EndsWith("Variable", StringComparison.Ordinal))
                {
                    wrong.Add($"{action.Key}: {name}");
                }
            }
        }

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
            ("x", "y"), ("offsetX", "offsetY"), ("startX", "startY"), ("endX", "endY"),
            ("width", "height"), ("deltaX", "deltaY"),
        };

        var split = new List<string>();

        foreach (var action in Ui.Run(() => ActionCatalog.RunnableActions.ToList()))
        {
            var pages = new[]
            {
                (Name: "base", Fields: On(action, viewModel => viewModel.Rows)),
                (Name: "advanced", Fields: On(action, viewModel => viewModel.AdvancedRows)),
                (Name: "output", Fields: On(action, viewModel => viewModel.OutputRows)),
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

    /// <summary>
    /// A step always has something to show: an action whose own fields are all on one page cannot
    /// open on a page that is not there.
    /// </summary>
    [Fact]
    public void A_step_always_opens_on_a_page_that_is_there()
    {
        var empty = new List<string>();

        Ui.Run(() =>
        {
            foreach (var action in ActionCatalog.RunnableActions)
            {
                var viewModel = Open(action.Key);
                if (viewModel.Page is not { IsPresent: true })
                {
                    empty.Add(action.Key);
                }
            }
        });

        Assert.Empty(empty);
    }

    /// <summary>
    /// The dialog opens at the size it wants when the desktop has the room for it, and at what is
    /// left of the desktop when it has not — never off the edge, and never under the size the four
    /// pages stop being readable in.
    /// </summary>
    [Theory]
    // A 1080p screen: the size it was designed at, with room to spare.
    [InlineData(1920, 1040, 1024, 800)]
    // The 1366 by 768 laptop: narrower than the four pages want, so it takes what there is.
    [InlineData(1366, 768, 1024, 688)]
    // A desktop too small even for the least it will open at, so it goes below what is left.
    [InlineData(800, 600, 900, 560)]
    public void The_dialog_opens_at_a_size_the_desktop_has_room_for(
        double roomWidth, double roomHeight, double width, double height)
    {
        Assert.Equal((width, height), DialogSize.Fit(roomWidth, roomHeight));
    }

    /// <summary>
    /// A step is written with the window it is going into in sight, so the dialog stands beside it
    /// whenever the desktop has room on either side — on the roomier one, level with the top of the
    /// window, and no wider than that room.
    /// </summary>
    [Theory]
    // A wide desktop with the editor in the middle: the whole form fits on its right.
    [InlineData(0, 0, 2560, 1040, 490, 100, 940, 780, 1430, 100, 1024)]
    // The editor pushed to the right: the room is now on its left.
    [InlineData(0, 0, 2560, 1040, 1500, 100, 940, 780, 476, 100, 1024)]
    // The same room on both sides: it takes the right.
    [InlineData(0, 0, 2600, 1040, 830, 100, 940, 780, 1770, 100, 830)]
    // Room on one side, but not the width the form would like: it takes what there is.
    [InlineData(0, 0, 1920, 1040, 0, 0, 940, 780, 940, 0, 980)]
    // The editor sits low: the form is pulled up to stay on the desktop.
    [InlineData(0, 0, 2560, 1040, 400, 700, 940, 780, 1340, 240, 1024)]
    // A second monitor beside the first: the room is measured from that desktop's own edge.
    [InlineData(1920, 0, 1920, 1040, 2000, 100, 940, 780, 2940, 100, 900)]
    public void The_dialog_stands_beside_the_window_it_was_opened_from_where_there_is_room(
        double desktopX, double desktopY, double desktopWidth, double desktopHeight,
        double windowX, double windowY, double windowWidth, double windowHeight,
        double x, double y, double width)
    {
        var beside = DialogSize.Beside(
            (windowX, windowY, windowWidth, windowHeight),
            (desktopX, desktopY, desktopWidth, desktopHeight),
            height: 800);

        Assert.Equal(x, beside!.Value.X);
        Assert.Equal(y, beside.Value.Y);
        Assert.Equal(width, beside.Value.Width);
    }

    /// <summary>
    /// With no room on either side there is nowhere to stand, and the dialog stays over the window
    /// it was opened from: two windows about a thousand pixels wide do not fit side by side on a
    /// 1080p desktop while the editor sits in the middle of it.
    /// </summary>
    [Fact]
    public void The_dialog_stays_over_the_window_when_neither_side_has_room()
    {
        Assert.Null(DialogSize.Beside((490, 100, 940, 780), (0, 0, 1920, 1040), height: 800));

        // And the 1366-wide laptop, where what is left over is narrower still.
        Assert.Null(DialogSize.Beside((213, 100, 940, 780), (0, 0, 1366, 768), height: 688));
    }

    /// <summary>
    /// The dialog stands beside the window it was opened from rather than over it, so the steps
    /// already written stay in sight while the next one is filled in.
    /// </summary>
    [Fact]
    public void The_dialog_opens_beside_the_window_that_asked_for_it()
    {
        Ui.Run(() =>
        {
            var owner = new Window { Width = 940, Height = 780 };
            owner.Show();
            Dispatcher.UIThread.RunJobs();

            var dialog = new AddActionWindow();
            dialog.ShowOver(owner);
            Dispatcher.UIThread.RunJobs();

            var screen = dialog.Screens.ScreenFromWindow(owner) ?? dialog.Screens.Primary!;
            var scale = screen.Scaling > 0 ? screen.Scaling : 1;
            var room = (screen.WorkingArea.Width / scale) - owner.Bounds.Width;

            // Standing beside the window takes a desktop with room on that side; one without is
            // what the check above covers.
            Assert.True(room >= DialogSize.SmallestBesideWidth,
                "the desktop this runs on has no room beside the window");

            // It starts where the window ends, on the same line, and takes what is left over.
            Assert.Equal(owner.Bounds.Width, dialog.Position.X);
            Assert.Equal(owner.Position.Y, dialog.Position.Y);
            Assert.Equal(Math.Min(DialogSize.WantedWidth, room), dialog.Width);

            dialog.Close();
            owner.Close();
            Dispatcher.UIThread.RunJobs();
        });
    }
}
