using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;

namespace WhaleGenie.ViewModels;

// The rows each page shows, and which page opens first.
public partial class AddActionViewModel
{
    /// <summary>
    /// Lays the parameters out over their pages, a line at a time, keeping the two halves of a
    /// screen position together so x and y sit side by side. Which page a field belongs on is
    /// decided here from the field itself: a name a step leaves its answer under is asked for on a
    /// page of its own, because that is a different question from what the step is.
    /// </summary>
    private void BuildRows()
    {
        Rows.Clear();
        AdvancedRows.Clear();

        var basic = new List<StepParameterViewModel>();
        var fine = new List<StepParameterViewModel>();
        foreach (var parameter in Parameters)
        {
            // The results are a page of their own rather than a line among the fields: what the
            // step hands over is a list of names to choose from, not one more setting.
            if (parameter.IsReturns)
            {
                continue;
            }

            // A call's arguments are a list too, and it is drawn where the step says which macro it
            // calls rather than as one more row of the form.
            if (parameter.Definition.Kind is ActionParameterKind.Arguments)
            {
                continue;
            }

            // A block's children live in the main editor list, where their nesting can be seen
            // and edited. Keeping their parameter out of the form leaves one structural editor.
            if (parameter.Definition.Kind is ActionParameterKind.Steps)
            {
                continue;
            }

            // A value a tool writes has no line of its own: the colours around a base colour are
            // picked off the picture, and a box repeating them would be a second way to say one
            // thing — the one that has to be kept in step by hand.
            if (parameter.WrittenByTool)
            {
                continue;
            }

            (parameter.IsAdvanced ? fine : basic).Add(parameter);
        }

        // A question is numbered once for the whole action, not once for each page. Some questions
        // have a part on each page — what to look for, and how the looking is tuned — and numbering
        // them per page gave two blocks called the same thing, both numbered 1, on two pages.
        var numbers = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var parameter in Parameters)
        {
            if (parameter.Group.Length > 0 && !numbers.ContainsKey(parameter.Group))
            {
                numbers[parameter.Group] = numbers.Count + 1;
            }
        }

        FillRows(basic, Rows, numbers);
        FillRows(fine, AdvancedRows, numbers);

        RefreshPages();
    }

    private static void FillRows(IEnumerable<StepParameterViewModel> parameters,
        ObservableCollection<ParameterRowViewModel> rows, IReadOnlyDictionary<string, int> numbers)
    {
        var laid = parameters.ToList();
        var index = 0;
        var group = string.Empty;
        while (index < laid.Count)
        {
            var current = laid[index];

            // A block opens on the first line of each page that answers it. A line that answers no
            // block leaves the counter alone, so a form without blocks reads exactly as it did
            // before blocks existed.
            var starts = !string.Equals(current.Group, group, StringComparison.Ordinal);
            if (starts)
            {
                group = current.Group;
            }

            // The two halves of a position share a line only while they answer the same block:
            // pairing across a block boundary would hang the second half under the wrong heading.
            var same = index + 1 < laid.Count
                && string.Equals(laid[index + 1].Group, current.Group, StringComparison.Ordinal);

            StepParameterViewModel? second = null;
            var third = (StepParameterViewModel?)null;
            var fourth = (StepParameterViewModel?)null;

            if (current.IsRegionQuad)
            {
                // 一个矩形的四个数站在同一行：左上角、宽、高一格接一格。它们是一句话，拆成两行
                // 只是把同一件事说两遍。
                foreach (var next in new[] { 1, 2, 3 })
                {
                    if (index + 1 >= laid.Count
                        || !string.Equals(laid[index + 1].Group, current.Group, StringComparison.Ordinal)
                        || !laid[index + 1].IsRegionQuad)
                    {
                        break;
                    }

                    var taken = laid[++index];
                    if (next == 1)
                    {
                        second = taken;
                    }
                    else if (next == 2)
                    {
                        third = taken;
                    }
                    else
                    {
                        fourth = taken;
                    }
                }
            }
            else if (same && (current.IsRowPair && laid[index + 1].IsRowPair
                              || current.IsCoordinate && laid[index + 1].IsCoordinate))
            {
                second = laid[++index];
            }

            // A field that belongs beside the line rather than on one of its own — the switch that
            // takes a mouse step's place from the pointer — rides on this line.
            var beside = fourth is null && index + 1 < laid.Count
                && string.Equals(laid[index + 1].Group, current.Group, StringComparison.Ordinal)
                && laid[index + 1].IsRowCompanion
                ? laid[++index]
                : null;

            // 四格行里第三格是宽，第四格是高；普通行里第三格是那个伴随字段。
            rows.Add(new ParameterRowViewModel(current, second, fourth is null ? beside : third, fourth)
            {
                GroupKey = current.Group,
                GroupIndex = numbers.GetValueOrDefault(current.Group),
                IsGroupStart = starts && current.Group.Length > 0,
            });

            // 条件这一格自带一整块编辑器（"全部成立"、行、树），块头已经把"条件"说过了，
            // 再挂一行同名的标签就是同一句话说两遍。
            if (starts && current.Group.Length > 0
                && current.Definition.Kind is ActionParameterKind.Condition)
            {
                current.HidesLabel = true;
            }

            index++;
        }
    }

    /// <summary>The lines of one page, which is what its mark and its presence are read from.</summary>
    private IReadOnlyList<ParameterRowViewModel> RowsOf(string key) => key switch
    {
        BasePage => Rows,
        AdvancedPage => AdvancedRows,
        _ => [],
    };

    /// <summary>
    /// True when a page holds something that is not the value it starts on. The returns page is a
    /// list of its own rather than rows of field editors, so it is asked about separately.
    /// </summary>
    private bool PageIsChanged(string key) => key switch
    {
        OtherPage => false,
        ExecPage => TunedStep,
        // A result nobody ticked is how every step is saved, so it is not a change worth opening
        // the page on; a primary result the catalogue hands over by default counts the same.
        ReturnsPage => ReturnsRows.Any(row => row.Ticked),
        _ => RowsOf(key).Any(row => row.IsApplicable && !row.IsDefault),
    };

    /// <summary>
    /// Says which pages there is anything to show on, and which of them holds something that is not
    /// the value it starts on. A page of a step's own fields with nothing on it is not shown at all,
    /// and a page holding a changed value carries a mark on its tab.
    /// </summary>
    private void RefreshPages()
    {
        foreach (var page in Pages)
        {
            // The page of step settings is about the step rather than about the action, so it is
            // there whatever the action is; the pages that hold an action's fields are not.
            var rows = RowsOf(page.Key);
            page.IsPresent = page.Key is OtherPage or ExecPage
                || rows.Any(row => row.IsApplicable)
                // An action whose answer is a result rather than a setting still has something to
                // show on the page that holds the settings: what it hands over is chosen there, and
                // a picture it is about to take is shown there. Without this the form would open on
                // the tuning page and the results could not be ticked at all.
                || page.Key is BasePage && (HasReturns || ShowsClipboardPreview);
            page.HasDot = page.Key is AdvancedPage or ReturnsPage or ExecPage && PageIsChanged(page.Key);
        }

        // A page can empty out while it is the one open — the questions about which hit to take
        // disappear when the way of recognising a picture changes — and something has to be on
        // screen, so the step moves to a page that is still there.
        if (Page is { IsPresent: false })
        {
            OpenFirstPage();
        }

        OnPropertyChanged(nameof(HasAdvanced));
        OnPropertyChanged(nameof(HasReturns));
    }

    /// <summary>
    /// Puts the step on the page worth reading first: for a step that already exists, the first one
    /// holding something that is not the value it starts on, so what makes it unusual is what is on
    /// screen; otherwise the first page that asks anything at all. A step being written has nothing
    /// worth jumping to — the names it leaves its answer under were filled in a moment ago — so it
    /// opens on what the step is.
    /// </summary>
    private void OpenFirstPage()
    {
        var present = Pages.Where(page => page.IsPresent).ToList();
        var filled = _isNewStep || !IsEditing
            ? null
            : present.FirstOrDefault(page => PageIsChanged(page.Key));

        Select(filled ?? present.FirstOrDefault() ?? Pages[0]);
    }

    /// <summary>
    /// Opens one page. The tab strip binds its selection to <see cref="Page"/> directly, so this is
    /// the way code asks for the same thing a click asks for.
    /// </summary>
    public void Select(ActionPageViewModel page)
    {
        Page = page;
    }
}
