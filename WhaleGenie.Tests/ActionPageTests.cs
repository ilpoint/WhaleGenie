using System;
using System.Collections.Generic;
using System.Linq;
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
    /// Every field of a step is on exactly one page. A field drawn in two places is a field with two
    /// answers being written to it, which is how a form comes to disagree with itself.
    /// </summary>
    [Fact]
    public void Every_field_belongs_to_one_page()
    {
        var clashes = new List<string>();

        foreach (var action in Ui.Run(() => ActionCatalog.RunnableActions.ToList()))
        {
            var pages = new[]
            {
                On(action, viewModel => viewModel.Rows),
                On(action, viewModel => viewModel.AdvancedRows),
                On(action, viewModel => viewModel.OutputRows),
            };

            var drawn = pages.SelectMany(names => names).ToList();
            var twice = drawn.GroupBy(name => name, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key);

            clashes.AddRange(twice.Select(name => $"{action.Key}: {name}"));
        }

        Assert.Empty(clashes);
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
}
