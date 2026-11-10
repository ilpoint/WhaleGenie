using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

// Elements of a window or a page: picking one, trying it, and narrowing a browser search.
public partial class ActionEditorView : UserControl
{
    /// <summary>
    /// Takes the control the pointer is put on through UI Automation and writes the selector it
    /// reads. The window that control sits in fills the action's window filter when that is still
    /// empty, which is what stops the selector from matching the same kind of control somewhere
    /// else.
    /// </summary>
    private async void OnPickElement(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: StepParameterViewModel parameter }
            || DataContext is not AddActionViewModel viewModel)
        {
            return;
        }

        if (await ElementPickerWindow.PickAsync(HostWindow!, Devices.Screen, Devices.Hook, Devices.Ui) is not { } picked)
        {
            return;
        }

        parameter.Text = picked.Selector;

        // What the picker reads is a title, so the rule goes back to holding the text up against
        // the title: one left on the process name or on a pattern would compare something other
        // than what was just picked and would recognise nothing.
        viewModel.FillWindowFromPick("window", picked.Window);
    }

    /// <summary>
    /// Opens the page a browser step is about and writes the selector of the element clicked on
    /// it. The address comes from the step's own address field, so "go to this page, then click
    /// that" is picked off the page the macro will actually be looking at. The profile comes from
    /// the step as well, so a step that works in a browser it has signed in to is picked off that
    /// same one.
    /// </summary>
    private async void OnPickBrowserElement(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: StepParameterViewModel parameter }
            || DataContext is not AddActionViewModel viewModel)
        {
            return;
        }

        var url = ParameterText(viewModel, "url");
        var profile = ParameterText(viewModel, "profile");

        try
        {
            var picked = await BrowserPicker.PickAsync(Devices.Browser, url, profile);
            if (picked.Selector.Length > 0)
            {
                // The steadiest way of naming the element goes into the field and the rest are
                // offered beside it: a page that has been redesigned is met by a second line, and
                // a second line is not something to spell out from memory.
                parameter.Text = picked.Selector;
                parameter.OfferCandidates(picked.Candidates);
            }
        }
        catch (Exception)
        {
            // The dialog can be dismissed while the page is open, and closing it takes the browser
            // down with it — there is nowhere left to show a message about that.
            if (!_closed)
            {
                await ConfirmDialog.ShowAsync(HostWindow!, Strings.Get("Add.PickBrowserElement"),
                    Strings.Get("Add.BrowserPickFailed"), Strings.Get("Common.Ok"), showCancel: false);
            }
        }
    }

    /// <summary>
    /// Flashes the control the selector names, so a pick can be checked without running the step.
    /// The dialog steps aside first, the same way picking does: the control being looked at is
    /// usually behind the window asking about it.
    /// </summary>
    private async void OnTestElement(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: StepParameterViewModel parameter }
            || DataContext is not AddActionViewModel viewModel)
        {
            return;
        }

        if (Selector(parameter) is not { Length: > 0 } selector)
        {
            await ReportTestAsync(Strings.Get("Add.TestElementEmpty"));
            return;
        }

        var filter = viewModel.WindowRuleFor("window");

        // Out of the way while the frame is up, the same way picking does it: the control is
        // usually behind the very window asking about it. The dialog waits the frame out, so it
        // never comes back on top of the thing it just said to look at.
        ScreenFrameWindow? flash = null;
        await AwayWhileAsync(async () =>
        {
            if (ElementFlash.Locate(viewModel.Ui, selector, filter) is { } found)
            {
                flash = ElementFlash.Show(found);
                if (flash is not null)
                {
                    await flash.Finished;
                }
            }
        });

        if (flash is null && !_closed)
        {
            await ReportTestAsync(Strings.Get("Add.TestElementMissing"));
        }
    }

    /// <summary>
    /// Outlines the element a page selector names on the page it belongs to, which is the same
    /// question the desktop picker answers next to it. It is answered the way a run would answer
    /// it — the lines of the field tried in order, the "which match" field read as well — so what
    /// is outlined is what the step will act on, and the message says which line that was.
    /// </summary>
    private async void OnTestBrowserElement(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: StepParameterViewModel parameter }
            || DataContext is not AddActionViewModel viewModel)
        {
            return;
        }

        var target = Target(viewModel);
        if (target.Selectors.Count == 0)
        {
            await ReportTestAsync(Strings.Get("Add.TestElementEmpty"));
            return;
        }

        var found = new HighlightResult(false, 0, 0);
        try
        {
            found = await BrowserPicker.HighlightAsync(Devices.Browser, target);
        }
        catch (Exception)
        {
            // The dialog can be dismissed while the page is being asked, and a window that is on
            // its way out is not worth reporting to.
        }

        if (_closed)
        {
            return;
        }

        await ReportTestAsync(found.Found
            ? Strings.Format("Add.TestPageElementFound", found.Line, found.Matches)
            : Strings.Get("Add.TestPageElementMissing"));
    }

    /// <summary>
    /// Adds one of the ways of naming the element a pick answered with as another line of the
    /// field, which is one more way for the step to find it once the first stops working.
    /// </summary>
    private void OnAddBrowserWay(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: BrowserWayViewModel way })
        {
            way.Take?.Invoke(way);
        }
    }

    /// <summary>Puts another thing the element has to be like at the end of the list.</summary>
    private void OnAddNarrowRow(object? sender, RoutedEventArgs e)
    {
        // The button is only drawn for a field that narrows an element down, but a hidden control
        // is still reachable from automation, and asking a condition field for a narrow family is
        // a question it has no answer to.
        if (sender is Control { DataContext: StepParameterViewModel { IsNarrow: true } parameter })
        {
            parameter.AddNarrowRow();
        }
    }

    /// <summary>
    /// Fills a row's value in by pointing at the page. What is pointed at is the element the row is
    /// about, and the way of narrowing decides what is taken off it: the writing it shows for a row
    /// that asks about text, and one of the ways of naming it for a row that asks about a container
    /// or about an element it holds.
    /// </summary>
    /// <remarks>
    /// The text is read off the page rather than taken from the way the pick named it, because the
    /// pick offers the ways that make a selector and a person adding a condition is after the words
    /// themselves. An element that shows no writing has none to take, which is said rather than
    /// left as a box that looks untouched.
    /// </remarks>
    private async void OnPickNarrowValue(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: NarrowRowViewModel row }
            || DataContext is not AddActionViewModel viewModel)
        {
            return;
        }

        var url = ParameterText(viewModel, "url");
        var profile = ParameterText(viewModel, "profile");

        try
        {
            var picked = await BrowserPicker.PickAsync(Devices.Browser, url, profile);
            if (picked.Selector.Length == 0)
            {
                return;
            }

            if (!row.WantsWords)
            {
                row.Value = picked.Selector;
                return;
            }

            var target = PageTarget.Of(picked.Selector, 1);
            var words = (await Task.Run(() =>
                Devices.Browser.Text(target, PagePart.Text, string.Empty, PickReadMs))).Trim();
            if (words.Length > 0)
            {
                row.Value = words;
            }
            else if (!_closed)
            {
                await ReportTestAsync(Strings.Get("Add.NarrowPickNoWords"));
            }
        }
        catch (Exception)
        {
            // The dialog can be dismissed while the page is open, and closing it takes the browser
            // down with it — there is nowhere left to show a message about that.
            if (!_closed)
            {
                await ConfirmDialog.ShowAsync(HostWindow!, Strings.Get("Add.PickBrowserElement"),
                    Strings.Get("Add.BrowserPickFailed"), Strings.Get("Common.Ok"), showCancel: false);
            }
        }
    }

    /// <summary>
    /// Looks at what the page holds with this row and the rows above it on top of the selector, and
    /// says how many elements that comes to. It is the one question a person adding a condition is
    /// asking — did this make it clearer — and it is answered by counting rather than by running the
    /// step, so a wrong condition is found before it acts on the wrong element.
    /// </summary>
    private async void OnLookNarrowRow(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: NarrowRowViewModel row }
            || DataContext is not AddActionViewModel viewModel)
        {
            return;
        }

        var target = Target(viewModel, row);
        if (target.Selectors.Count == 0)
        {
            await ReportTestAsync(Strings.Get("Add.TestElementEmpty"));
            return;
        }

        var found = new HighlightResult(false, 0, 0);
        try
        {
            found = await BrowserPicker.HighlightAsync(Devices.Browser, target);
        }
        catch (Exception)
        {
            // The dialog can be dismissed while the page is being asked, and a window that is on
            // its way out is not worth reporting to.
        }

        if (!_closed)
        {
            row.Say(found);
        }
    }

    /// <summary>
    /// The element a browser step means, read out of the form the way a run reads it: the selectors
    /// the field holds, which match the step means, and what the step says the element has to be
    /// like. With <paramref name="upto"/> named the rows after that one are left out, which is how
    /// one row is looked at on top of the rows above it.
    /// </summary>
    private static PageTarget Target(AddActionViewModel viewModel, NarrowRowViewModel? upto = null)
        => PageTarget.Of(ParameterText(viewModel, "pageSelector"), MatchIndex(viewModel),
            [.. (viewModel.Parameters.FirstOrDefault(parameter => parameter.IsNarrow)?.Conditions(upto)
                    ?? [])
                .Select(condition => new PageCondition(
                    Enum.TryParse<PageFilter>(condition.How, ignoreCase: true, out var how)
                        ? how
                        : PageFilter.HasText,
                    condition.Value))]);

    /// <summary>
    /// Writes the name of a variable into a selector field, on a line of its own: a macro that works
    /// out which element to look for puts the selector in a variable and names it here, and the
    /// engine fills the name in when the step runs.
    /// </summary>
    private async void OnPickBrowserVariable(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: StepParameterViewModel parameter }
            || DataContext is not AddActionViewModel viewModel)
        {
            return;
        }

        // The expression editor is not offered: a selector field is read as text with the names in
        // it filled in, so a formula built here would be written down as part of the selector.
        if (await ValuePickerWindow.ShowFor(HostWindow!, null, viewModel.CollectVariables(),
                namesOnly: true) is not { Length: > 0 } picked)
        {
            return;
        }

        parameter.AddLine(picked);
    }

    /// <summary>
    /// Which of the elements a selector finds this step means. The test reads it the way a run
    /// does, so that a step set to work on the second match is checked on that one.
    /// </summary>
    private static int MatchIndex(AddActionViewModel viewModel)
        => int.TryParse(ParameterText(viewModel, "matchIndex").Trim(), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var index) && index >= 1
            ? index
            : 1;

    /// <summary>The selector a field holds, or null when nothing has been picked or written yet.</summary>
    private static string? Selector(StepParameterViewModel parameter)
    {
        var text = (parameter.Text ?? string.Empty).Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>The text of a parameter of this action, or an empty string when it has none.</summary>
    private static string ParameterText(AddActionViewModel viewModel, string name)
        => viewModel.Parameters.FirstOrDefault(item => item.Definition.Name == name)?.Text ?? string.Empty;

    /// <summary>Says what the test found, without leaving the dialog.</summary>
    private async Task ReportTestAsync(string message)
        => await ConfirmDialog.ShowAsync(HostWindow!, Strings.Get("Add.TestElement"), message,
            Strings.Get("Common.Ok"), showCancel: false);
}
