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
        if (e.OldItems is not null)
        {
            foreach (TriggerEntry entry in e.OldItems)
            {
                entry.PropertyChanged -= OnEntryChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (TriggerEntry entry in e.NewItems)
            {
                entry.PropertyChanged += OnEntryChanged;
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnEntryChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        => Changed?.Invoke(this, EventArgs.Empty);

    private void OnSurfaceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => Changed?.Invoke(this, EventArgs.Empty);
}
