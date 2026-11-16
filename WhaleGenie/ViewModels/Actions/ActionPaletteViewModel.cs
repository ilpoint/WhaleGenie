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
            ActionPaletteDock.SubMacros => Strings.Get("Palette.NoSubMacros"),
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

    /// <summary>
    /// 一张卡片答不答得上这句搜索。先按整句去对；对不上时再按两个字一段拆开，每一段都要在
    /// 卡片里出现过。拆开来是因为动作名现在按族分开了（「点击界面元素」与「点击网页元素」），
    /// 而人还是照老说法问「点击元素」——整句已经不在名字里，两个字一段仍在。
    /// </summary>
    private static bool Answers(ActionPaletteCardViewModel card, string search)
    {
        if (search.Length == 0 || Says(card, search))
        {
            return true;
        }

        // 两个字一段地拆才有意义：只拆一个字时，几乎每一张卡片都答得上，搜索就不再是搜索。
        if (search.Length < 4)
        {
            return false;
        }

        for (var at = 0; at < search.Length; at += 2)
        {
            if (!Says(card, search.Substring(at, Math.Min(2, search.Length - at))))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>True when one piece of the question appears in what the card says about itself.</summary>
    private static bool Says(ActionPaletteCardViewModel card, string text)
        => card.Key.Contains(text, StringComparison.OrdinalIgnoreCase)
           || card.Title.Contains(text, StringComparison.OrdinalIgnoreCase)
           || card.Description.Contains(text, StringComparison.OrdinalIgnoreCase)
           || card.CategoryLabel.Contains(text, StringComparison.OrdinalIgnoreCase);

    private void Rebuild()
    {
        var search = Search.Trim();
        Cards.Clear();
        Sections.Clear();
        var wanted = SelectedCategory?.Value;

        // 一输入搜索词，就不再分「基础动作 / 流程控制」两个页签：动作库是一个库，人问「延迟」
        // 的时候不该先知道它归哪一栏。页签仍然管浏览，清空搜索就回到原来那一页；分类芯片仍管
        // 收窄——那是人自己点的筛子，搜索不该把它扔了。
        var everywhere = search.Length > 0
            && SelectedTab.Dock is ActionPaletteDock.Basic or ActionPaletteDock.Flow;

        IEnumerable<ActionPaletteCardViewModel> source = SelectedTab.Dock switch
        {
            _ when everywhere => _availableActions
                .Where(action => wanted is null
                    || ActionCategoryPresentation.PickerCategory(action.Category) == wanted)
                .OrderBy(action => ActionCategoryPresentation.PickerCategory(action.Category))
                .ThenBy(action => action.LocalName)
                .Select(ActionCard),

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

        foreach (var card in source.Where(card => Answers(card, search)))
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
            or "control.runMacro" => "Waiting",
        // 变量与列表已经归到「数据与变量」这一分类里，走基础动作那一栏；这里只剩向人取一个
        // 值的两步，它们仍属流程上的一个停顿。
        "control.ask" or "control.input" => "Data",
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
