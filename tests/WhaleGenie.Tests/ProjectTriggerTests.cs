using System.IO;
using System.Text.Json.Nodes;
using WhaleGenie.Core.Devices;
using WhaleGenie.Models;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;
using WhaleGenie.Editing;

namespace WhaleGenie.Tests;

public class ProjectTriggerTests
{
    [Fact]
    public void A_window_trigger_can_match_one_exact_instance_by_handle()
    {
        var rule = new WindowRule(Title: "Game", Handle: 12, How: WindowHow.Handle);
        var devices = new ChecksDevices();

        Assert.True(ProjectTriggers.WindowRuleMatches(rule,
            new WindowInfo(12, "Game", new ScreenPoint(0, 0), new ScreenSize(100, 100), false, false),
            devices.Windows));
        Assert.False(ProjectTriggers.WindowRuleMatches(rule,
            new WindowInfo(13, "Game", new ScreenPoint(0, 0), new ScreenSize(100, 100), false, false),
            devices.Windows));
    }

    [Fact]
    public void One_macro_is_listed_once_in_every_view_it_belongs_to()
    {
        var viewModel = new MainViewModel();
        var macro = new MacroItem { Name = "multi" };
        List<TriggerEntry> entries =
        [
            new() { MacroName = macro.Name, Spec = new KeySpec("F8") },
            new() { MacroName = macro.Name, Spec = new InterfaceSpec(new WindowRule(Title: "Game")) },
            new() { MacroName = macro.Name, Spec = new HotstringSpec(";go") },
            new() { MacroName = macro.Name, Spec = new MenuSpec() },
        ];

        viewModel.AddMacro(macro, entries);

        foreach (var view in new[]
                 {
                     MacroView.All, MacroView.Keys, MacroView.Interface,
                     MacroView.Hotstring, MacroView.Menu,
                 })
        {
            Assert.True(viewModel.Triggers.InView("multi", view));
        }

        // It is on no wheel position and starts nothing on a schedule or a watch.
        Assert.False(viewModel.Triggers.InView("multi", MacroView.Wheel));
        Assert.False(viewModel.Triggers.InView("multi", MacroView.Events));
    }

    /// <summary>
    /// A macro with no trigger is listed in "all" and nowhere else. It used to be counted as a key
    /// macro, which made a new macro look as if someone had already chosen a trigger for it.
    /// </summary>
    [Fact]
    public void A_macro_without_a_trigger_is_only_in_the_all_view()
    {
        var triggers = new ProjectTriggers();
        Assert.True(triggers.InView("nothing", MacroView.All));
        Assert.False(triggers.InView("nothing", MacroView.Keys));
        Assert.False(triggers.InView("nothing", MacroView.Menu));
    }

    [Fact]
    public void A_macro_placed_on_the_wheel_is_listed_under_the_wheel_view()
    {
        var viewModel = new MainViewModel();
        var macro = new MacroItem { Name = "wheeled" };
        viewModel.AddMacro(macro);
        viewModel.Triggers.Entries.Clear();
        viewModel.Triggers.Wheel.Global.At(new WheelSlotRef(WheelRing.Inner, 0)).Macro = "wheeled";

        Assert.True(viewModel.Triggers.InView("wheeled", MacroView.Wheel));
        Assert.False(viewModel.Triggers.InView("wheeled", MacroView.Menu));
    }

    /// <summary>
    /// "Add" pressed on the menu or the wheel view puts the new macro where that view keeps things.
    /// It happens after the save, because a cell and a position both name the macro.
    /// </summary>
    [Fact]
    public void A_macro_created_from_the_menu_or_wheel_view_is_placed_there()
    {
        var viewModel = new MainViewModel();

        var menuMacro = new MacroItem { Name = "from-menu" };
        viewModel.AddMacro(menuMacro);
        ProjectMacroCommit.PlaceNewMacro(MacroView.Menu, menuMacro, viewModel.Workspace);

        var cell = Assert.Single(viewModel.Triggers.Entries);
        Assert.Equal(TriggerKind.Menu, cell.Kind);
        Assert.Equal("from-menu", cell.MacroName);
        var cellSpec = Assert.IsType<MenuSpec>(cell.Spec);
        Assert.Equal(viewModel.Triggers.CommonMenuPages()[0].Id, cellSpec.PageId);
        Assert.Equal(0, cellSpec.Row);
        Assert.Equal(0, cellSpec.Column);

        // The next one takes the cell after it, not the same one again.
        var second = new MacroItem { Name = "second" };
        viewModel.AddMacro(second);
        ProjectMacroCommit.PlaceNewMacro(MacroView.Menu, second, viewModel.Workspace);
        var secondCell = Assert.IsType<MenuSpec>(viewModel.Triggers.Entries
            .Single(entry => entry.MacroName == "second").Spec);
        Assert.Equal(0, secondCell.Row);
        Assert.Equal(1, secondCell.Column);

        var wheelMacro = new MacroItem { Name = "from-wheel" };
        viewModel.AddMacro(wheelMacro);
        ProjectMacroCommit.PlaceNewMacro(MacroView.Wheel, wheelMacro, viewModel.Workspace);

        Assert.Equal("from-wheel", viewModel.Triggers.Wheel.Global.Inner[0].Macro);
        Assert.True(viewModel.Triggers.InView("from-wheel", MacroView.Wheel));
    }

    [Fact]
    public void A_watched_pixel_belongs_to_the_event_view()
    {
        var viewModel = new MainViewModel();
        var colour = new MacroItem { Name = "colour" };
        var timer = new MacroItem { Name = "timer" };

        viewModel.AddMacro(colour, [new TriggerEntry { MacroName = "colour", Spec = new ColorSpec() }]);
        viewModel.AddMacro(timer, [new TriggerEntry { MacroName = "timer", Spec = new TimerSpec() }]);

        Assert.True(viewModel.Triggers.InView("colour", MacroView.Events));
        Assert.True(viewModel.Triggers.InView("timer", MacroView.Events));
        Assert.False(viewModel.Triggers.InView("colour", MacroView.Keys));
    }

    [Fact]
    public void A_trigger_collection_round_trips_through_json()
    {
        var triggers = new ProjectTriggers();
        triggers.Entries.Add(new TriggerEntry
        {
            MacroName = "macro",
            Spec = new HotstringSpec("sig", CaseSensitive: true, InsideWord: true,
                RequireEndChar: false, EraseTyped: false, KeepEndChar: false),
        });
        triggers.Entries.Add(new TriggerEntry
        {
            MacroName = "macro",
            Spec = new InterfaceSpec(new WindowRule(Title: "Notepad")),
        });
        triggers.StoreLayout(new WindowRule(Title: "Notepad"), 30, 70);
        triggers.StoreInterfaceHotkey(new WindowRule(Title: "Notepad"), "Ctrl+Alt+N");
        var page = triggers.AddContextMenuPage(new WindowRule(Title: "Notepad"));
        triggers.Entries.Add(new TriggerEntry
        {
            MacroName = "macro",
            Spec = new MenuSpec(page.Id, 1, 2, new MenuIconReference(MenuIconKind.Builtin, "Play")),
        });

        var restored = ProjectTriggers.FromJson(JsonNode.Parse(triggers.ToJson().ToJsonString())
            as JsonObject);

        Assert.Equal(3, restored.Entries.Count);
        var hotstring = Assert.IsType<HotstringSpec>(restored.Entries[0].Spec);
        Assert.Equal("sig", hotstring.Abbreviation);
        Assert.True(hotstring.InsideWord);
        Assert.Equal(30, Assert.Single(restored.InterfaceLayouts).OffsetX);
        Assert.Equal("Ctrl+Alt+N", Assert.Single(restored.InterfaceLayouts).ToggleHotkey);
        Assert.Single(restored.CommonMenuPages());
        Assert.Single(restored.ContextMenuPages());
        var menu = Assert.IsType<MenuSpec>(
            restored.Entries.Single(entry => entry.Kind == TriggerKind.Menu).Spec);
        Assert.Equal((1, 2), (menu.Row, menu.Column));
        Assert.Equal(new MenuIconReference(MenuIconKind.Builtin, "Play"), menu.Icon);
    }

    [Fact]
    public void A_process_rule_matches_every_window_owned_by_that_program()
    {
        var windows = new WindowsForChecks()
            .Add(1, "Notes - first", "notepad", "Notepad")
            .Add(2, "Notes - second", "notepad", "Notepad")
            .Add(3, "Browser", "chrome", "Chrome_WidgetWin_1");
        var rule = new WindowRule(Process: "notepad", How: WindowHow.ProcessEquals);
        var triggers = new ProjectTriggers();
        triggers.BlockedMenuRules.Add(rule);

        Assert.True(triggers.IsMenuBlocked(windows.Open[0], windows));
        Assert.True(triggers.IsMenuBlocked(windows.Open[1], windows));
        Assert.False(triggers.IsMenuBlocked(windows.Open[2], windows));
    }

    [Fact]
    public void Blocked_program_rules_survive_project_json()
    {
        var triggers = new ProjectTriggers();
        var rule = new WindowRule(Process: "game", How: WindowHow.ProcessEquals);
        triggers.BlockedMenuRules.Add(rule);

        var restored = ProjectTriggers.FromJson(JsonNode.Parse(triggers.ToJson().ToJsonString())
            as JsonObject);

        Assert.Equal(rule, Assert.Single(restored.BlockedMenuRules));
    }

    [Fact]
    public void A_quick_process_rule_is_reused_instead_of_duplicated()
    {
        var windows = new WindowsForChecks().Add(1, "Notes", "notepad", "Notepad");
        var triggers = new ProjectTriggers();

        var first = triggers.EnsureProcessMenuRule(windows.Open[0], "notepad", "Notepad");
        var second = triggers.EnsureProcessMenuRule(windows.Open[0], "Notepad", "Notepad");

        Assert.Same(first, second);
        var group = Assert.Single(triggers.ContextRuleGroups());
        Assert.Equal(WindowHow.ProcessEquals, group.Rule.How);
        Assert.Equal("notepad", group.Rule.Process);
    }

    [Fact]
    public void Blocking_the_same_program_twice_keeps_one_rule()
    {
        var windows = new WindowsForChecks().Add(1, "Notes", "notepad", "Notepad");
        var triggers = new ProjectTriggers();

        Assert.True(triggers.BlockMenuForProcess(windows.Open[0], "notepad", "Notepad"));
        Assert.False(triggers.BlockMenuForProcess(windows.Open[0], "Notepad", "Notepad"));

        Assert.Single(triggers.BlockedMenuRules);
    }

    [Fact]
    public void Renaming_a_macro_keeps_every_entry_pointing_at_it()
    {
        var triggers = new ProjectTriggers();
        triggers.Entries.Add(new TriggerEntry { MacroName = "old", Spec = new KeySpec() });
        triggers.Entries.Add(new TriggerEntry
        {
            MacroName = "old",
            Spec = new MenuSpec(triggers.CommonMenuPages()[0].Id),
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
            MacroName = "first",
            Spec = new InterfaceSpec(new WindowRule(Title: "Game")),
        });
        triggers.Entries.Add(new TriggerEntry
        {
            MacroName = "second",
            Spec = new InterfaceSpec(new WindowRule(Title: "Game")),
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
            MacroName = "one",
            Spec = new MenuSpec(page.Id, 0, 0),
        });
        var next = new TriggerEntry { MacroName = "two", Spec = new MenuSpec() };

        triggers.AssignFirstEmptyMenuCell(next, page.Id);

        var cell = Assert.IsType<MenuSpec>(next.Spec);
        Assert.Equal((0, 1), (cell.Row, cell.Column));
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
                MacroName = macro.Name,
                Spec = new HotstringSpec(";r"),
            });
            var context = triggers.AddContextMenuPage(new WindowRule(Title: "Notepad"));
            triggers.Entries.Add(new TriggerEntry
            {
                MacroName = macro.Name,
                Spec = new MenuSpec(context.Id, 0, 0,
                    new MenuIconReference(MenuIconKind.Builtin, "play")),
            });

            RecoveryStore.SaveProject([macro], triggers, [], null);
            var loaded = RecoveryStore.Load();

            Assert.NotNull(loaded);
            Assert.Contains(loaded!.Triggers.Entries, entry =>
                entry.Spec is HotstringSpec { Abbreviation: ";r" });
            Assert.Single(loaded.Triggers.ContextMenuPages());
            Assert.Contains(loaded.Triggers.Entries, entry =>
                entry.Spec is MenuSpec
                {
                    Icon: { Kind: MenuIconKind.Builtin, Value: "play" },
                });
        }
        finally
        {
            RecoveryStore.Clear();
            RecoveryStore.FilePath = stored;
            Directory.Delete(folder, true);
        }
    }
}
