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

public partial class SettingsWindow
{
    /// <summary>
    /// Which page is open: the tabs at the top, the list on the left, and the
    /// selection the two of them share.
    /// </summary>
    /// <summary>Shows the page the panel's selection stands for.</summary>
    private void OnNavSelectionChanged(object? sender, FANavigationViewSelectionChangedEventArgs e)
    {
        if (PageOf(e.SelectedItemContainer) is { } page)
        {
            _viewModel.SetPage(page);
        }
    }

    /// <summary>Opens the group one of the tabs at the top of the window stands for.</summary>

    /// <summary>Opens the group one of the tabs at the top of the window stands for.</summary>
    private void OnRootTabClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && Enum.TryParse<SettingsRoot>(tag, out var root))
        {
            _viewModel.SetRoot(root);
        }
    }

    /// <summary>
    /// Leaves only the pages of the open group in the list. The items are folded rather than taken
    /// out of <c>MenuItems</c>: a panel whose list is rebuilt keeps its selection on an item it no
    /// longer holds. Disabling them as well keeps Home, End and the arrow keys off a page that is
    /// not on screen.
    /// </summary>

    /// <summary>
    /// Leaves only the pages of the open group in the list. The items are folded rather than taken
    /// out of <c>MenuItems</c>: a panel whose list is rebuilt keeps its selection on an item it no
    /// longer holds. Disabling them as well keeps Home, End and the arrow keys off a page that is
    /// not on screen.
    /// </summary>
    private void ShowMenuItemsOf(SettingsSubPage page)
    {
        var root = SettingsViewModel.RootOf(page);
        foreach (var item in _nav.MenuItems.OfType<FANavigationViewItem>())
        {
            var mine = PageOf(item) is { } tag && SettingsViewModel.RootOf(tag) == root;
            item.IsVisible = mine;
            item.IsEnabled = mine;
        }
    }

    /// <summary>
    /// Highlights the item that stands for a page. The item carries the page as its tag, so the
    /// mapping lives beside the item rather than in a second list here.
    /// </summary>

    /// <summary>
    /// Highlights the item that stands for a page. The item carries the page as its tag, so the
    /// mapping lives beside the item rather than in a second list here.
    /// </summary>
    private static void SelectPage(FANavigationView nav, SettingsSubPage page)
    {
        foreach (var item in nav.MenuItems.OfType<FANavigationViewItem>())
        {
            if (PageOf(item) == page)
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

    private static SettingsSubPage? PageOf(object? item)
        => item is FANavigationViewItem { Tag: string tag }
           && Enum.TryParse<SettingsSubPage>(tag, out var page)
            ? page
            : null;
}
