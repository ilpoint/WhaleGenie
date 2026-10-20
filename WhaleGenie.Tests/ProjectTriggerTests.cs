using System.IO;
using System.Text.Json.Nodes;
using WhaleGenie.Core.Devices;
using WhaleGenie.Models;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Tests;

public class ProjectTriggerTests
{
    [Fact]
    public void One_macro_can_appear_on_several_dock_pages()
    {
        var viewModel = new MainViewModel();
        viewModel.SelectedDock = DockPage.Keys;
        var macro = new MacroItem { Name = "multi" };
        macro.Triggers.Add(new TriggerEntry { Kind = TriggerKind.Key, BindKey = "F8" });
        macro.Triggers.Add(new TriggerEntry
        {
            Kind = TriggerKind.Interface,
            WindowTarget = new WindowRule(Title: "Game"),
        });
        macro.Triggers.Add(new TriggerEntry
        {
            Kind = TriggerKind.Hotstring,
            Abbreviation = ";go",
        });
        macro.Triggers.Add(new TriggerEntry { Kind = TriggerKind.Menu });

        viewModel.AddMacro(macro);

        Assert.Single(viewModel.VisibleMacros);
        viewModel.SelectedDock = DockPage.Interface;
        Assert.Single(viewModel.VisibleMacros);
        viewModel.SelectedDock = DockPage.Hotstring;
        Assert.Single(viewModel.VisibleMacros);
        viewModel.SelectedDock = DockPage.Menu;
        Assert.Single(viewModel.VisibleMacros);
    }

    [Fact]
    public void The_five_dock_pages_put_existing_triggers_where_the_product_says()
    {
        var viewModel = new MainViewModel();
        viewModel.SelectedDock = DockPage.Keys;
        var colour = new MacroItem { Name = "colour" };
        colour.Triggers.Add(new TriggerEntry { Kind = TriggerKind.Color });
        var timer = new MacroItem { Name = "timer" };
        timer.Triggers.Add(new TriggerEntry { Kind = TriggerKind.Timer });
        var empty = new MacroItem { Name = "empty" };
        empty.Triggers.Clear();

        viewModel.AddMacro(colour);
        viewModel.AddMacro(timer);
        viewModel.AddMacro(empty);

        Assert.Contains(viewModel.VisibleMacros, macro => macro.Name == "colour");
        viewModel.SelectedDock = DockPage.Other;
        Assert.Equal(["timer"], viewModel.VisibleMacros.Select(macro => macro.Name));
    }

    [Fact]
    public void A_trigger_collection_round_trips_through_json()
    {
        var triggers = new ProjectTriggers();
        triggers.Entries.Add(new TriggerEntry
        {
            Kind = TriggerKind.Hotstring,
            MacroName = "macro",
            Abbreviation = "sig",
            CaseSensitive = true,
            MatchInsideWord = true,
            RequireEndChar = false,
            EraseTyped = false,
            KeepEndChar = false,
        });
        triggers.Entries.Add(new TriggerEntry
        {
            Kind = TriggerKind.Interface,
            MacroName = "macro",
            WindowTarget = new WindowRule(Title: "Notepad"),
        });
        triggers.StoreLayout(new WindowRule(Title: "Notepad"), 30, 70);
        triggers.StoreInterfaceHotkey(new WindowRule(Title: "Notepad"), "Ctrl+Alt+N");
        triggers.MenuLauncher = "Ctrl+Alt+M";
        var page = triggers.AddContextMenuPage(new WindowRule(Title: "Notepad"));
        triggers.Entries.Add(new TriggerEntry
        {
            Kind = TriggerKind.Menu,
            MacroName = "macro",
            MenuLabel = "Open",
            MenuPageId = page.Id,
            MenuRow = 1,
            MenuColumn = 2,
            MenuIcon = new MenuIconReference(MenuIconKind.Builtin, "play"),
        });

        var restored = ProjectTriggers.FromJson(JsonNode.Parse(triggers.ToJson().ToJsonString())
            as JsonObject);

        Assert.Equal(3, restored.Entries.Count);
        Assert.Equal("sig", restored.Entries[0].Abbreviation);
        Assert.True(restored.Entries[0].MatchInsideWord);
        Assert.Equal("Ctrl+Alt+M", restored.MenuLauncher);
        Assert.Equal(30, Assert.Single(restored.InterfaceLayouts).OffsetX);
        Assert.Equal("Ctrl+Alt+N", Assert.Single(restored.InterfaceLayouts).ToggleHotkey);
        Assert.Single(restored.CommonMenuPages());
        Assert.Single(restored.ContextMenuPages());
        var menu = restored.Entries.Single(entry => entry.Kind == TriggerKind.Menu);
        Assert.Equal("Open", menu.MenuLabel);
        Assert.Equal((1, 2), (menu.MenuRow, menu.MenuColumn));
        Assert.Equal(new MenuIconReference(MenuIconKind.Builtin, "play"), menu.MenuIcon);
    }

    [Fact]
    public void Renaming_a_macro_keeps_every_entry_pointing_at_it()
    {
        var triggers = new ProjectTriggers();
        triggers.Entries.Add(new TriggerEntry { Kind = TriggerKind.Key, MacroName = "old" });
        triggers.Entries.Add(new TriggerEntry
        {
            Kind = TriggerKind.Menu,
            MacroName = "old",
            MenuPageId = triggers.CommonMenuPages()[0].Id,
        });

        triggers.RenameMacro("old", "new");

        Assert.All(triggers.Entries, entry => Assert.Equal("new", entry.MacroName));
    }

    [Fact]
    public void The_same_interface_rule_keeps_all_of_its_macros_in_one_group()
    {
        var triggers = new ProjectTriggers();
        triggers.Entries.Add(new TriggerEntry
        {
            Kind = TriggerKind.Interface,
            MacroName = "first",
            WindowTarget = new WindowRule(Title: "Game"),
        });
        triggers.Entries.Add(new TriggerEntry
        {
            Kind = TriggerKind.Interface,
            MacroName = "second",
            WindowTarget = new WindowRule(Title: "Game"),
        });

        var chosen = Assert.Single(triggers.InterfaceGroups());

        Assert.Equal(2, chosen.Entries.Count);
    }

    [Fact]
    public void A_new_menu_entry_uses_the_first_empty_cell()
    {
        var triggers = new ProjectTriggers();
        var page = triggers.CommonMenuPages()[0];
        triggers.Entries.Add(new TriggerEntry
        {
            Kind = TriggerKind.Menu,
            MacroName = "one",
            MenuPageId = page.Id,
            MenuRow = 0,
            MenuColumn = 0,
        });
        var next = new TriggerEntry { Kind = TriggerKind.Menu, MacroName = "two" };

        triggers.AssignFirstEmptyMenuCell(next, page.Id);

        Assert.Equal((0, 1), (next.MenuRow, next.MenuColumn));
    }

    [Fact]
    public void A_recovery_snapshot_keeps_trigger_entries()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wg-triggers-" + Guid.NewGuid().ToString("N"));
        var stored = RecoveryStore.FilePath;
        Directory.CreateDirectory(folder);
        try
        {
            RecoveryStore.FilePath = Path.Combine(folder, "recovery.json");
            var macro = new MacroItem { Name = "saved" };
            var triggers = new ProjectTriggers();
            triggers.Entries.Add(new TriggerEntry
            {
                Kind = TriggerKind.Hotstring,
                MacroName = macro.Name,
                Abbreviation = ";r",
            });
            triggers.MenuLauncher = "F9";
            var context = triggers.AddContextMenuPage(new WindowRule(Title: "Notepad"));
            triggers.Entries.Add(new TriggerEntry
            {
                Kind = TriggerKind.Menu,
                MacroName = macro.Name,
                MenuPageId = context.Id,
                MenuRow = 0,
                MenuColumn = 0,
                MenuIcon = new MenuIconReference(MenuIconKind.Builtin, "play"),
            });

            RecoveryStore.SaveProject([macro], triggers, [], null);
            var loaded = RecoveryStore.Load();

            Assert.NotNull(loaded);
            Assert.Contains(loaded!.Triggers.Entries, entry =>
                entry.Kind == TriggerKind.Hotstring && entry.Abbreviation == ";r");
            Assert.Equal("F9", loaded.Triggers.MenuLauncher);
            Assert.Single(loaded.Triggers.ContextMenuPages());
            Assert.Contains(loaded.Triggers.Entries, entry =>
                entry.Kind == TriggerKind.Menu
                && entry.MenuIcon == new MenuIconReference(MenuIconKind.Builtin, "play"));
            Assert.Equal(2, Assert.Single(loaded.Macros).Triggers.Count);
        }
        finally
        {
            RecoveryStore.Clear();
            RecoveryStore.FilePath = stored;
            Directory.Delete(folder, true);
        }
    }
}
