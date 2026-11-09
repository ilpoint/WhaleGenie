using WhaleGenie.Core.Devices;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Tests;

/// <summary>
/// The trigger kinds and their dropdowns. Which setting a dropdown shows is picked by its place
/// in the list, so a kind or a choice added to an enum without a line in the list would quietly
/// put up the wrong one.
/// </summary>
public class TriggerKindTests
{
    [Fact]
    public void Every_trigger_kind_is_offered_and_shows_its_own_settings()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel();
            TriggerKind[] offered =
            [
                TriggerKind.Key,
                TriggerKind.Color,
                TriggerKind.Interface,
                TriggerKind.Hotstring,
                TriggerKind.Timer,
                TriggerKind.File,
                TriggerKind.Process,
                TriggerKind.Window,
                TriggerKind.Idle,
            ];

            Assert.Equal(offered, editor.TriggerKindChoices.Select(choice => choice.Kind));

            foreach (var kind in offered)
            {
                editor.AddTriggerCommand.Execute(kind);

                Assert.Equal(kind, editor.TriggerMode);
                Assert.Equal(kind == TriggerKind.Key, editor.IsKeyTrigger);
                Assert.Equal(kind == TriggerKind.Color, editor.IsColorTrigger);
                Assert.Equal(kind == TriggerKind.Interface, editor.IsInterfaceTrigger);
                Assert.Equal(kind == TriggerKind.Hotstring, editor.IsHotstringTrigger);
                Assert.False(editor.IsMenuTrigger);
                Assert.Equal(kind == TriggerKind.Timer, editor.IsTimerTrigger);
                Assert.Equal(kind == TriggerKind.File, editor.IsFileTrigger);
                Assert.Equal(kind == TriggerKind.Process, editor.IsProcessTrigger);
                Assert.Equal(kind == TriggerKind.Window, editor.IsWindowTrigger);
                Assert.Equal(kind is TriggerKind.Window or TriggerKind.Interface,
                    editor.IsWindowRuleTrigger);
                Assert.Equal(kind == TriggerKind.Idle, editor.IsIdleTrigger);
            }
        });
    }

    [Fact]
    public void Trigger_entries_can_be_added_by_kind_and_removed_one_card_at_a_time()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel { Name = "probe" };
            editor.AddTriggerCommand.Execute(TriggerKind.Key);
            var key = editor.SelectedTrigger!;
            editor.BindKey = "F8";

            editor.AddTriggerCommand.Execute(TriggerKind.Color);
            var firstColor = editor.SelectedTrigger!;
            editor.AddTriggerCommand.Execute(TriggerKind.Color);
            var secondColor = editor.SelectedTrigger!;

            Assert.Equal(3, editor.TriggerEntries.Count);
            Assert.Equal(TriggerKind.Color, firstColor.Kind);
            Assert.Equal(TriggerKind.Color, secondColor.Kind);
            Assert.Equal("F8", key.BindKey);
            Assert.Same(secondColor, editor.SelectedTrigger);

            editor.RemoveTriggerCommand.Execute(firstColor);

            Assert.DoesNotContain(firstColor, editor.TriggerEntries);
            Assert.Same(secondColor, editor.SelectedTrigger);

            var menu = new TriggerEntry { Kind = TriggerKind.Menu };
            editor.TriggerEntries.Add(menu);
            editor.SelectedTrigger = menu;
            editor.RemoveTriggerCommand.Execute(menu);

            Assert.Contains(menu, editor.TriggerEntries);
            Assert.False(menu.IsRemovable);
        });
    }

    [Fact]
    public void Shared_trigger_fields_follow_the_selected_entry_kind()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel();
            TriggerKind[] kinds =
            [
                TriggerKind.Key,
                TriggerKind.Color,
                TriggerKind.Interface,
                TriggerKind.Hotstring,
                TriggerKind.Timer,
                TriggerKind.File,
                TriggerKind.Process,
                TriggerKind.Window,
                TriggerKind.Idle,
            ];

            foreach (var kind in kinds)
            {
                editor.AddTriggerCommand.Execute(kind);

                Assert.True(editor.ShowsTriggerSettings);
                Assert.Equal(kind == TriggerKind.Key, editor.ShowsLoopMode);
                Assert.Equal(kind is TriggerKind.Key or TriggerKind.Color or TriggerKind.Timer
                    or TriggerKind.File or TriggerKind.Process or TriggerKind.Window or TriggerKind.Idle,
                    editor.ShowsTriggerOnce);
            }
        });
    }

    [Fact]
    public void Switching_entries_commits_the_old_fields_and_loads_the_new_ones()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel { Name = "probe" };
            editor.AddTriggerCommand.Execute(TriggerKind.Key);
            var key = editor.SelectedTrigger!;
            editor.BindKey = "F8";
            editor.TriggerOnce = true;

            editor.AddTriggerCommand.Execute(TriggerKind.Timer);
            var timer = editor.SelectedTrigger!;
            editor.ScheduleMode = ScheduleMode.Daily;
            editor.ScheduleTime = "21:15";
            editor.TriggerOnce = true;

            editor.SelectedTrigger = key;
            Assert.Equal(TriggerKind.Key, editor.TriggerMode);
            Assert.Equal("F8", editor.BindKey);
            Assert.True(editor.TriggerOnce);

            editor.BindKey = "F9";
            editor.SelectedTrigger = timer;
            Assert.Equal(TriggerKind.Timer, editor.TriggerMode);
            Assert.Equal(ScheduleMode.Daily, editor.ScheduleMode);
            Assert.Equal("21:15", editor.ScheduleTime);
            Assert.True(editor.TriggerOnce);

            editor.SelectedTrigger = key;
            Assert.Equal("F9", editor.BindKey);
        });
    }

    /// <summary>
    /// A macro created from a view starts with that view's entry: a key entry with nothing bound
    /// yet, a window entry, or a string entry. "All", "events", the menu and the wheel are not one
    /// kind, so they start empty — the last two are placed after the macro has a name.
    /// </summary>
    [Fact]
    public void A_new_macro_starts_with_the_entry_its_view_stands_for()
    {
        Ui.Run(() =>
        {
            (MacroView View, TriggerKind? Kind)[] cases =
            [
                (MacroView.Keys, TriggerKind.Key),
                (MacroView.Interface, TriggerKind.Interface),
                (MacroView.Hotstring, TriggerKind.Hotstring),
                (MacroView.All, null),
                (MacroView.Events, null),
                (MacroView.Menu, null),
                (MacroView.Wheel, null),
            ];

            foreach (var (view, kind) in cases)
            {
                var editor = new MacroEditorViewModel { Name = "probe" };
                editor.PrepareNewTrigger(view);

                if (kind is { } wanted)
                {
                    Assert.Equal(wanted, Assert.Single(editor.TriggerEntries).Kind);
                    Assert.Same(editor.TriggerEntries[0], editor.SelectedTrigger);
                }
                else
                {
                    Assert.Empty(editor.TriggerEntries);
                }
            }
        });
    }

    /// <summary>
    /// A macro nobody bound anything to is an ordinary state: no trigger, and it repeats once
    /// rather than until the key is pressed again.
    /// </summary>
    [Fact]
    public void A_new_macro_has_no_trigger_and_repeats_once()
    {
        var viewModel = new MainViewModel();
        viewModel.AddMacro(new MacroItem { Name = "fresh" });

        Assert.Empty(viewModel.Triggers.ForMacro("fresh"));
        Assert.True(viewModel.Triggers.InView("fresh", MacroView.All));
        Assert.False(viewModel.Triggers.InView("fresh", MacroView.Keys));

        Assert.Equal(MacroLoop.Press, new MacroItem().LoopMode);
        Assert.Equal(MacroLoop.Press, new TriggerEntry().LoopMode);

        // A file written without the field is read with the same default, not with the old one.
        var macro = MacroItem.FromJson(new System.Text.Json.Nodes.JsonObject { ["name"] = "Old" });
        Assert.Equal(MacroLoop.Press, macro.LoopMode);
        Assert.Equal(MacroLoop.Press,
            TriggerEntry.FromJson(new System.Text.Json.Nodes.JsonObject()).LoopMode);
    }

    [Fact]
    public void Every_choice_a_trigger_offers_picks_the_setting_it_stands_for()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel();
            Assert.Equal(editor.ScheduleOptions.Count, Enum.GetValues<ScheduleMode>().Length);
            Assert.Equal(editor.ScheduleUnitOptions.Count, Enum.GetValues<ScheduleUnit>().Length);
            Assert.Equal(editor.WatchChangeOptions.Count, Enum.GetValues<FileChangeKind>().Length);
            Assert.Equal(editor.ProcessChangeOptions.Count, Enum.GetValues<ProcessChangeKind>().Length);
            Assert.Equal(editor.WindowChangeOptions.Count, Enum.GetValues<WindowChangeKind>().Length);

            foreach (var mode in Enum.GetValues<ScheduleMode>())
            {
                editor.ScheduleIndex = (int)mode;
                Assert.Equal(mode, editor.ScheduleMode);
            }

            foreach (var unit in Enum.GetValues<ScheduleUnit>())
            {
                editor.ScheduleUnitIndex = (int)unit;
                Assert.Equal(unit, editor.ScheduleUnit);
            }

            foreach (var change in Enum.GetValues<FileChangeKind>())
            {
                editor.WatchChangeIndex = (int)change;
                Assert.Equal(change, editor.WatchChange);
            }

            foreach (var change in Enum.GetValues<ProcessChangeKind>())
            {
                editor.ProcessChangeIndex = (int)change;
                Assert.Equal(change, editor.ProcessChange);
            }

            foreach (var change in Enum.GetValues<WindowChangeKind>())
            {
                editor.WindowChangeIndex = (int)change;
                Assert.Equal(change, editor.WindowChange);
            }

        });
    }

    [Fact]
    public void The_settings_of_a_trigger_survive_a_trip_through_the_editor()
    {
        Ui.Run(() =>
        {
            var windowRule = new WindowRule(
                Title: "Setup",
                Process: "notepad",
                ClassName: "NotepadClass",
                Handle: 42,
                How: WindowHow.Class,
                CaseSensitive: true,
                Trim: false,
                Multiline: true);
            var macro = new MacroItem { Name = "Probe" };
            macro.Triggers.Add(new TriggerEntry
            {
                Kind = TriggerKind.Process,
                ProcessName = " notepad ",
                ProcessChange = ProcessChangeKind.Stopped,
                TriggerOnce = true,
            });
            macro.Triggers.Add(new TriggerEntry
            {
                Kind = TriggerKind.File,
                WatchPath = " notes.txt ",
                WatchChange = FileChangeKind.Created,
                WatchFilter = "*.log",
                WatchSubfolders = true,
            });
            macro.Triggers.Add(new TriggerEntry
            {
                Kind = TriggerKind.Timer,
                ScheduleMode = ScheduleMode.Daily,
                ScheduleTime = "21:15",
                ScheduleInterval = 30,
                ScheduleUnit = ScheduleUnit.Minutes,
            });
            macro.Triggers.Add(new TriggerEntry
            {
                Kind = TriggerKind.Window,
                WindowTarget = windowRule,
                WindowChange = WindowChangeKind.Disappeared,
            });
            macro.Triggers.Add(new TriggerEntry
            {
                Kind = TriggerKind.Idle,
                IdleSeconds = 90,
            });

            var editor = new MacroEditorViewModel();
            editor.LoadFrom(macro);

            // Each entry has to be the selected one once, because that is when the fields on the
            // page are written back into it.
            foreach (var entry in editor.TriggerEntries.ToList())
            {
                editor.SelectedTrigger = entry;
            }

            MacroItem? saved = null;
            editor.CloseRequested += built => saved = built;
            editor.SaveCommand.Execute(null);

            Assert.NotNull(saved);
            var process = saved.Triggers.Single(entry => entry.Kind == TriggerKind.Process);
            Assert.Equal("notepad", process.ProcessName);
            Assert.Equal(ProcessChangeKind.Stopped, process.ProcessChange);
            Assert.True(process.TriggerOnce);

            var file = saved.Triggers.Single(entry => entry.Kind == TriggerKind.File);
            Assert.Equal("notes.txt", file.WatchPath);
            Assert.Equal(FileChangeKind.Created, file.WatchChange);
            Assert.Equal("*.log", file.WatchFilter);
            Assert.True(file.WatchSubfolders);

            var timer = saved.Triggers.Single(entry => entry.Kind == TriggerKind.Timer);
            Assert.Equal(ScheduleMode.Daily, timer.ScheduleMode);
            Assert.Equal("21:15", timer.ScheduleTime);
            Assert.Equal(30, timer.ScheduleInterval);
            Assert.Equal(ScheduleUnit.Minutes, timer.ScheduleUnit);

            var window = saved.Triggers.Single(entry => entry.Kind == TriggerKind.Window);
            Assert.Equal(windowRule, window.WindowTarget);
            Assert.Equal(WindowChangeKind.Disappeared, window.WindowChange);

            Assert.Equal(90, saved.Triggers.Single(entry => entry.Kind == TriggerKind.Idle).IdleSeconds);
        });
    }

    [Fact]
    public void A_trigger_the_editor_has_not_filled_in_gets_usable_defaults()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel { Name = "probe" };
            editor.WatchFilter = "   ";
            editor.ScheduleInterval = null;

            MacroItem? saved = null;
            editor.CloseRequested += built => saved = built;
            editor.SaveCommand.Execute(null);

            Assert.NotNull(saved);
            Assert.Equal("*", saved.WatchFilter);
            Assert.Equal(5, saved.ScheduleInterval);
            Assert.Equal(60, saved.IdleSeconds);
            Assert.Equal(ScheduleMode.Interval, saved.ScheduleMode);
            Assert.Equal(ScheduleUnit.Seconds, saved.ScheduleUnit);
        });
    }

    [Fact]
    public void A_program_trigger_is_previewed_with_the_program()
    {
        var macro = new MacroItem
        {
            TriggerMode = MacroTrigger.Process,
            ProcessName = "notepad",
            ProcessChange = ProcessChangeKind.Stopped,
        };

        Assert.True(macro.IsProcessTrigger);
        Assert.Equal("notepad", macro.TriggerPreview);

        var restored = MacroItem.FromJson(macro.ToJson());

        Assert.Equal("notepad", restored.ProcessName);
        Assert.Equal(ProcessChangeKind.Stopped, restored.ProcessChange);
    }

    [Fact]
    public void A_window_trigger_is_previewed_with_the_window_and_survives_being_saved()
    {
        var macro = new MacroItem
        {
            TriggerMode = MacroTrigger.Window,
            WindowTarget = new WindowRule(Title: "Save As"),
            WindowChange = WindowChangeKind.Appeared,
        };

        Assert.True(macro.IsWindowTrigger);
        Assert.Equal("Save As", macro.TriggerPreview);

        var restored = MacroItem.FromJson(macro.ToJson());

        Assert.Equal(new WindowRule(Title: "Save As"), restored.WindowTarget);
        Assert.Equal(WindowChangeKind.Appeared, restored.WindowChange);
    }

    [Fact]
    public void A_macro_written_before_the_window_trigger_existed_waits_for_a_title()
    {
        var macro = MacroItem.FromJson(new System.Text.Json.Nodes.JsonObject { ["name"] = "Old" });

        Assert.Equal(WindowRule.Empty, macro.WindowTarget);
        Assert.Equal(WindowChangeKind.Appeared, macro.WindowChange);
    }

    [Fact]
    public void A_macro_written_before_the_program_trigger_existed_reads_back_as_a_start()
    {
        var macro = MacroItem.FromJson(new System.Text.Json.Nodes.JsonObject { ["name"] = "Old" });

        Assert.Equal(string.Empty, macro.ProcessName);
        Assert.Equal(ProcessChangeKind.Started, macro.ProcessChange);
    }

    [Fact]
    public void An_idle_trigger_is_previewed_with_its_wait_and_survives_being_saved()
    {
        var macro = new MacroItem
        {
            TriggerMode = MacroTrigger.Idle,
            IdleSeconds = 120,
        };

        Assert.True(macro.IsIdleTrigger);
        Assert.Contains("120", macro.TriggerPreview);

        Assert.Equal(120, MacroItem.FromJson(macro.ToJson()).IdleSeconds);
    }

    [Fact]
    public void A_macro_written_before_the_idle_trigger_existed_waits_a_minute()
    {
        var macro = MacroItem.FromJson(new System.Text.Json.Nodes.JsonObject { ["name"] = "Old" });

        Assert.Equal(60, macro.IdleSeconds);
    }
}
