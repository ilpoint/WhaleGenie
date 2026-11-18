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

/// <summary>
/// A step's settings are read a page at a time, and which page a field is on is a question about
/// the field: what the step is, how it does it, or the name it leaves its answer under. Those are
/// rules about the whole catalogue rather than about one action, so they are checked over it.
/// </summary>
public partial class ActionPageTests
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

        // A field standing at the end of a line is on that line's page rather than on one of its
        // own, so it counts as a field the page draws like any other.
        if (row.Third is { } third)
        {
            yield return third.Definition.Name;
        }

        // 一个矩形的四个数一行说完，第四个也在这条线上。
        if (row.Fourth is { } fourth)
        {
            yield return fourth.Definition.Name;
        }
    }
}
