using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

public partial class AdvancedSettingsWindow
{
    /// <summary>
    /// Which group is open: the list on the left, the page tabs above the content column, and the
    /// page the two of them share.
    /// </summary>
    private void OnNavSelectionChanged(object? sender, FANavigationViewSelectionChangedEventArgs e)
    {
        // Opening the page a group is already on must not reset it to that group's first page:
        // the item is written when the page changes, and that write comes back here.
        if (RootOf(e.SelectedItemContainer) is { } root && _viewModel.SelectedRoot != root)
        {
            _viewModel.SetRoot(root);
        }
    }

    /// <summary>Opens one of the pages named at the top of the content column.</summary>
    private void OnSubPageClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && Enum.TryParse<SettingsSubPage>(tag, out var page))
        {
            _viewModel.SetPage(page);
        }
    }

    /// <summary>
    /// Highlights the group the open page belongs to. The item carries the group as its tag, so the
    /// mapping lives beside the item rather than in a second list here.
    /// </summary>
    private static void SelectRoot(FANavigationView nav, SettingsSubPage page)
    {
        var root = SettingsViewModel.RootOf(page);
        foreach (var item in nav.MenuItems.OfType<FANavigationViewItem>())
        {
            if (RootOf(item) == root)
            {
                // Writing the same item back raises no change, so the page and the list settle on
                // the first pass instead of passing the change back and forth.
                if (!ReferenceEquals(nav.SelectedItem, item))
                {
                    nav.SelectedItem = item;
                }

                return;
            }
        }
    }

    private static SettingsRoot? RootOf(object? item)
        => item is FANavigationViewItem { Tag: string tag }
           && Enum.TryParse<SettingsRoot>(tag, out var root)
            ? root
            : null;
}

