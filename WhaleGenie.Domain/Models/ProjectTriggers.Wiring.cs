using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;


namespace WhaleGenie.Models;

// The pages a project always has, and the change notices that keep the entries in step.
public sealed partial class ProjectTriggers
{
    /// <summary>The entries this set is listening to, so a clear can let go of all of them.</summary>
    private readonly HashSet<TriggerEntry> _watched = [];

    private void EnsureMenuPages()
    {
        if (CommonMenuPages().Count == 0)
        {
            MenuPages.Insert(0,
                new MenuPage("common-1", MenuPageRegion.Common, WindowRule.Empty));
        }

        EnsureDefaultContextPage();
    }

    private MenuPage EnsureDefaultContextPage()
    {
        if (MenuPages.FirstOrDefault(page =>
                page.Region == MenuPageRegion.Context && page.IsDefault) is { } existing)
        {
            return existing;
        }

        var page = new MenuPage("default", MenuPageRegion.Context,
            WindowRule.Empty, IsDefault: true);
        MenuPages.Add(page);
        return page;
    }

    private static string NewMenuPageId() => Guid.NewGuid().ToString("N")[..10];

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var entry in _watched)
        {
            entry.PropertyChanged -= OnEntryChanged;
        }

        _watched.Clear();
        foreach (var entry in Entries)
        {
            entry.PropertyChanged += OnEntryChanged;
            _watched.Add(entry);
        }

        RaiseChanged();
    }

    private void OnEntryChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        => RaiseChanged();

    private void OnSurfaceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RaiseChanged();
}
