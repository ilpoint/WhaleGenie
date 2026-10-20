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

            Assert.Equal(offered.Length, editor.TriggerOptions.Count);

            for (var index = 0; index < offered.Length; index++)
            {
                var kind = offered[index];
                editor.TriggerIndex = index;

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
            var macro = new MacroItem
            {
                Name = "Probe",
                TriggerMode = MacroTrigger.Process,
                ProcessName = " notepad ",
                ProcessChange = ProcessChangeKind.Stopped,
                TriggerOnce = true,
                WatchPath = " notes.txt ",
                WatchChange = FileChangeKind.Created,
                WatchFilter = "*.log",
                WatchSubfolders = true,
                ScheduleMode = ScheduleMode.Daily,
                ScheduleTime = "21:15",
                ScheduleInterval = 30,
                ScheduleUnit = ScheduleUnit.Minutes,
                WindowTarget = windowRule,
                WindowChange = WindowChangeKind.Disappeared,
                IdleSeconds = 90,
            };

            var editor = new MacroEditorViewModel();
            editor.LoadFrom(macro);

            MacroItem? saved = null;
            editor.CloseRequested += built => saved = built;
            editor.SaveCommand.Execute(null);

            Assert.NotNull(saved);
            Assert.Equal(MacroTrigger.Process, saved.TriggerMode);
            Assert.Equal("notepad", saved.ProcessName);
            Assert.Equal(ProcessChangeKind.Stopped, saved.ProcessChange);
            Assert.True(saved.TriggerOnce);
            Assert.Equal("notes.txt", saved.WatchPath);
            Assert.Equal(FileChangeKind.Created, saved.WatchChange);
            Assert.Equal("*.log", saved.WatchFilter);
            Assert.True(saved.WatchSubfolders);
            Assert.Equal(ScheduleMode.Daily, saved.ScheduleMode);
            Assert.Equal("21:15", saved.ScheduleTime);
            Assert.Equal(30, saved.ScheduleInterval);
            Assert.Equal(ScheduleUnit.Minutes, saved.ScheduleUnit);
            Assert.Equal(windowRule, saved.WindowTarget);
            Assert.Equal(WindowChangeKind.Disappeared, saved.WindowChange);
            Assert.Equal(90, saved.IdleSeconds);
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
