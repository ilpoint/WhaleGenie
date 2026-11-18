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

/// <summary>
/// Every trigger entry and trigger-surface setting in the project. A macro body can be reached by
/// any number of these entries; none of them changes the macro's steps.
/// </summary>
public sealed partial class ProjectTriggers
{
    public const int MenuColumns = 4;
    public const int MenuRegionRows = 3;
    public const int MaxCommonMenuPages = 8;

    /// <summary>
    /// How many writes are being made as one. A replace takes every entry out and puts new ones in;
    /// without this, each of those steps would announce a whole new project to the list, the menu
    /// and the runtime.
    /// </summary>
    private int _batch;

    public ProjectTriggers()
    {
        Entries.CollectionChanged += OnCollectionChanged;
        InterfaceLayouts.CollectionChanged += OnSurfaceCollectionChanged;
        MenuPages.CollectionChanged += OnSurfaceCollectionChanged;
        BlockedMenuRules.CollectionChanged += OnSurfaceCollectionChanged;
        EnsureMenuPages();
    }

    public ObservableCollection<TriggerEntry> Entries { get; } = [];

    public ObservableCollection<InterfaceLayout> InterfaceLayouts { get; } = [];

    public ObservableCollection<MenuPage> MenuPages { get; } = [];

    /// <summary>
    /// The wheel's positions and the window rules that choose them. They belong to the project
    /// rather than to this machine: a position names a macro, and a macro only exists in a project.
    /// The button that opens the wheel is a machine choice and stays in <c>wheel-menu.json</c>.
    /// </summary>
    public WheelMenuRules Wheel { get; private set; } = new();

    /// <summary>Programs whose foreground windows do not open the macro menu.</summary>
    public ObservableCollection<WindowRule> BlockedMenuRules { get; } = [];

    public event EventHandler? Changed;

    public void NotifyChanged() => RaiseChanged();

    /// <summary>Holds the change notices back until the matching <see cref="EndBatch"/>.</summary>
    public void BeginBatch() => _batch++;

    public void EndBatch()
    {
        if (_batch > 0 && --_batch == 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Announces a change unless the caller is still writing the batch it belongs to.</summary>
    private void RaiseChanged()
    {
        if (_batch == 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
