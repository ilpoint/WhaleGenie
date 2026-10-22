using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Lucide.Avalonia;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;

namespace WhaleGenie.ViewModels;

public enum ActionPaletteDock
{
    Basic,
    Flow,
    SubMacros,
    PublicMacros,
}

public enum ActionPaletteCardKind
{
    Action,
    SubMacro,
    PublicMacro,
}

public sealed record ActionPaletteTab(ActionPaletteDock Dock, string Label, LucideIconKind Icon);

public sealed record ActionPaletteSection(string Title, IReadOnlyList<ActionPaletteCardViewModel> Cards)
{
    public bool HasTitle => Title.Length > 0;
}

public sealed class ActionPaletteCardViewModel
{
    public required ActionPaletteCardKind Kind { get; init; }

    public required string Title { get; init; }

    public string Description { get; init; } = string.Empty;

    public string Meta { get; init; } = string.Empty;

    /// <summary>The category badge shown on an action card, empty for submacros and public macros.</summary>
    public string CategoryLabel { get; init; } = string.Empty;

    public bool HasCategoryLabel => CategoryLabel.Length > 0;

    public bool HasMeta => Meta.Length > 0;

    public IBrush Accent { get; init; } = Brushes.Transparent;

    public LucideIconKind Icon { get; init; } = AppIcons.Workflow;

    public string Key { get; init; } = string.Empty;

    public MacroItem? Macro { get; init; }

    public PublicMacroEntry? PublicMacro { get; init; }
}

/// <summary>The left-hand library. Its four docks choose a source rather than a second tree of actions.</summary>
public partial class ActionPaletteViewModel : ViewModelBase
{
    private IReadOnlyList<MacroItem> _subMacros = [];
    private IReadOnlyList<PublicMacroEntry> _publicMacros = [];
    private IReadOnlyList<ActionDefinition> _availableActions = ActionCatalog.RunnableActions;

    public ActionPaletteViewModel()
    {
        Tabs = BuildTabs();
        SelectedTab = Tabs[0];
        Categories = ActionCategoryPresentation.BasicCategories();
        SelectedCategory = Categories[0];
        Rebuild();
    }

    public IReadOnlyList<ActionPaletteTab> Tabs { get; private set; }

    /// <summary>The categories shown only inside the basic-actions dock.</summary>
    public IReadOnlyList<ActionCategoryChoice> Categories { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBasicDock))]
    [NotifyPropertyChangedFor(nameof(IsFlowDock))]
    [NotifyPropertyChangedFor(nameof(IsSubMacroDock))]
    [NotifyPropertyChangedFor(nameof(IsPublicMacroDock))]
    public partial ActionPaletteTab SelectedTab { get; set; }

    public bool IsBasicDock => SelectedTab.Dock == ActionPaletteDock.Basic;
    public bool IsFlowDock => SelectedTab.Dock == ActionPaletteDock.Flow;
    public bool IsSubMacroDock => SelectedTab.Dock == ActionPaletteDock.SubMacros;
    public bool IsPublicMacroDock => SelectedTab.Dock == ActionPaletteDock.PublicMacros;

    [ObservableProperty]
    public partial ActionCategoryChoice? SelectedCategory { get; set; }

    [ObservableProperty]
    public partial string Search { get; set; } = string.Empty;

    public ObservableCollection<ActionPaletteCardViewModel> Cards { get; } = [];

    public ObservableCollection<ActionPaletteSection> Sections { get; } = [];

    public bool HasNoCards => Cards.Count == 0;

    public string EmptyText => Search.Trim().Length > 0
        ? Strings.Get("Add.NoActionFound")
        : SelectedTab.Dock switch
        {
            ActionPaletteDock.SubMacros => Strings.Get("Palette.NoCalledSubMacros"),
            ActionPaletteDock.PublicMacros => Strings.Get("Palette.NoPublicMacros"),
            _ => Strings.Get("Add.NoActionFound"),
        };

    partial void OnSelectedTabChanged(ActionPaletteTab value) => Rebuild();

    partial void OnSelectedCategoryChanged(ActionCategoryChoice? value) => Rebuild();

    partial void OnSearchChanged(string value) => Rebuild();

    public void SelectDock(ActionPaletteDock dock)
        => SelectedTab = Tabs.First(tab => tab.Dock == dock);

    public void SetSources(IReadOnlyList<ActionDefinition>? actions,
        IReadOnlyList<MacroItem> subMacros, IReadOnlyList<PublicMacroEntry> publicMacros)
    {
        _availableActions = actions ?? ActionCatalog.RunnableActions;
        _subMacros = subMacros;
        _publicMacros = publicMacros;
        Rebuild();
    }

    public override void RefreshLanguage()
    {
        var dock = SelectedTab.Dock;
        var category = SelectedCategory?.Value;
        Tabs = BuildTabs();
        OnPropertyChanged(nameof(Tabs));
        Categories = ActionCategoryPresentation.BasicCategories();
        OnPropertyChanged(nameof(Categories));
        SelectedCategory = Categories.First(choice => choice.Value == category);
        SelectedTab = Tabs.First(tab => tab.Dock == dock);
        Rebuild();
    }

    private void Rebuild()
    {
        var search = Search.Trim();
        Cards.Clear();
        Sections.Clear();
        var wanted = SelectedCategory?.Value;

        IEnumerable<ActionPaletteCardViewModel> source = SelectedTab.Dock switch
        {
            ActionPaletteDock.Basic => _availableActions
                .Where(action => action.Category != ActionCategory.Control)
                .Where(action => wanted is null
                    || ActionCategoryPresentation.PickerCategory(action.Category) == wanted)
                .OrderBy(action => ActionCategoryPresentation.PickerCategory(action.Category))
                .ThenBy(action => action.LocalName)
                .Select(ActionCard),
            ActionPaletteDock.Flow => _availableActions
                .Where(action => action.Category == ActionCategory.Control)
                .OrderBy(action => action.Key)
                .Select(ActionCard),
            ActionPaletteDock.SubMacros => _subMacros
                .OrderBy(macro => macro.Name)
                .Select(SubMacroCard),
            ActionPaletteDock.PublicMacros => _publicMacros
                .OrderBy(entry => entry.Macro.Name)
                .Select(PublicCard),
            _ => [],
        };

        foreach (var card in source.Where(card =>
                     search.Length == 0
                     || card.Key.Contains(search, StringComparison.OrdinalIgnoreCase)
                     || card.Title.Contains(search, StringComparison.OrdinalIgnoreCase)
                     || card.Description.Contains(search, StringComparison.OrdinalIgnoreCase)
                     || card.CategoryLabel.Contains(search, StringComparison.OrdinalIgnoreCase)))
        {
            Cards.Add(card);
        }

        if (SelectedTab.Dock == ActionPaletteDock.Flow)
        {
            foreach (var group in Cards
                         .GroupBy(card => FlowGroup(card.Key))
                         .OrderBy(group => FlowGroupRank(group.Key)))
            {
                Sections.Add(new ActionPaletteSection(
                    Strings.Get($"Palette.Flow.{group.Key}"), [.. group]));
            }
        }
        else if (Cards.Count > 0)
        {
            Sections.Add(new ActionPaletteSection(string.Empty, [.. Cards]));
        }

        OnPropertyChanged(nameof(HasNoCards));
        OnPropertyChanged(nameof(EmptyText));
    }

    private static ActionPaletteCardViewModel ActionCard(ActionDefinition action)
    {
        var category = ActionCategoryPresentation.PickerCategory(action.Category);
        return new ActionPaletteCardViewModel
        {
            Kind = ActionPaletteCardKind.Action,
            Key = action.Key,
            Title = action.LocalName,
            Description = action.LocalDescription,
            CategoryLabel = Strings.Get($"Add.Category.{category}"),
            Accent = ActionCategoryPresentation.Accent(category),
            Icon = ActionCategoryPresentation.Icon(category),
        };
    }

    private static string FlowGroup(string key) => key switch
    {
        "control.repeat" or "control.while" or "control.for"
            or "control.forEach" or "control.if" or "control.switch" or "control.try" => "Branch",
        "control.break" or "control.continue" or "control.return" or "control.rethrow"
            or "control.stop" or "control.fail" => "Jump",
        "control.delay" or "control.waitUntil"
            or "control.holdInput" or "control.holdBrowser"
            or "control.runMacro" or "control.startMacro" or "control.waitMacro"
            or "control.waitAll" or "control.waitAny" or "control.runStatus"
            or "control.cancelMacro" => "Waiting",
        "control.setVariable" or "control.incrementGlobal"
            or "control.listAdd" or "control.listInsert"
            or "control.listRemoveAt" or "control.listSet" or "control.listClear"
            or "control.ask" or "control.input" => "Data",
        "control.note" or "control.log" => "Notes",
        _ => "Other",
    };

    private static int FlowGroupRank(string key) => key switch
    {
        "Branch" => 0,
        "Jump" => 1,
        "Waiting" => 2,
        "Data" => 3,
        "Notes" => 4,
        _ => 5,
    };

    private static ActionPaletteCardViewModel SubMacroCard(MacroItem macro) => new()
    {
        Kind = ActionPaletteCardKind.SubMacro,
        Key = macro.Name,
        Title = macro.Name,
        Description = macro.Description,
        Meta = macro.Origin is { } origin
            ? Strings.Format("Palette.PublicMacroVersion", origin.Name, origin.Version)
            : Strings.Get("Palette.LocalSubMacro"),
        Icon = AppIcons.Workflow,
        Accent = ActionCategoryPresentation.Accent(ActionCategory.Control),
        Macro = macro,
    };

    private static ActionPaletteCardViewModel PublicCard(PublicMacroEntry entry) => new()
    {
        Kind = ActionPaletteCardKind.PublicMacro,
        Key = entry.Id,
        Title = entry.Macro.Name,
        Description = entry.Macro.Description,
        Meta = Strings.Format("Palette.MacroVersion", entry.Version),
        Icon = AppIcons.Folder,
        Accent = ActionCategoryPresentation.Accent(ActionCategory.Browser),
        Macro = entry.Macro,
        PublicMacro = entry,
    };

    private static IReadOnlyList<ActionPaletteTab> BuildTabs() =>
    [
        new(ActionPaletteDock.Basic, Strings.Get("Editor.Dock.BasicActions"), AppIcons.GridFour),
        new(ActionPaletteDock.Flow, Strings.Get("Editor.Dock.Flow"), AppIcons.Workflow),
        new(ActionPaletteDock.SubMacros, Strings.Get("Editor.Dock.SubMacros"), AppIcons.ExternalLink),
        new(ActionPaletteDock.PublicMacros, Strings.Get("Editor.Dock.PublicMacros"), AppIcons.Folder),
    ];
}
