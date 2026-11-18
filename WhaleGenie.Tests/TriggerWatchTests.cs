using System.IO;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Models;
using WhaleGenie.Runtime;

namespace WhaleGenie.Tests;

/// <summary>
/// A trigger being waited for: what it is waiting for, when that was last looked at, and what the
/// clock does when the machine has nothing to say. Both ways a window is noticed — the machine
/// saying it changed, and the clock coming round — end in the same place, so what a machine that
/// says nothing leaves behind is the part worth checking.
/// </summary>
public class TriggerWatchTests
{
    [Fact]
    public void A_machine_that_says_nothing_leaves_the_window_list_to_the_clock()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem
            {
                Name = "等一个不存在的窗口",
                TriggerMode = MacroTrigger.Window,
                WindowTarget = new WindowRule(Title: "鲸灵等不到的这个窗口标题"),
                WindowChange = WindowChangeKind.Appeared,
            };
            macro.Steps.Add(new MacroStep { Type = "control.stop" });

            using var service = new MacroTriggerService(
                () => [macro], new WindowsDeviceLayer(), new NoWindowEvents());

            var before = DateTime.Now;
            service.Look();

            var watch = Assert.Single(service.Watches());
            Assert.False(watch.Told);       // nothing is saying when a window changes
            Assert.False(watch.Matched);    // and this one is not there
            Assert.True(watch.Checked >= before);
            Assert.True(watch.Next > watch.Checked);
        });
    }

    /// <summary>
    /// A window trigger recognises its window the way every other window rule does, so a title that
    /// moves with the document can be answered by the program that owns the window or by a pattern
    /// instead. What is waited for and what the list says are read off the same rule.
    /// </summary>
    [Fact]
    public void A_window_trigger_is_recognised_the_way_the_rule_says()
    {
        Ui.RunAsync(async () =>
        {
            var windows = new WindowsForChecks().Add(1, "Untitled - Notepad", "notepad", "Notepad");

            var macro = new MacroItem
            {
                Name = "等这个程序",
                TriggerMode = MacroTrigger.Window,
                WindowTarget = new WindowRule(Process: "notepad", How: WindowHow.Process),
                WindowChange = WindowChangeKind.Appeared,
            };
            macro.Steps.Add(new MacroStep { Type = "control.stop" });

            using var service = new MacroTriggerService(
                () => [macro], new ChecksDevices { Windows = windows }, new NoWindowEvents());

            service.Look();

            // The window the rule names is open, which is what the reading says: nothing here is
            // about the title, because the rule is not either.
            Assert.True(Assert.Single(service.Watches()).Matched);

            // A rule nothing answers to waits, rather than being taken for the window it is not.
            // The window list is looked at every half second, which is what the wait is for.
            macro.WindowTarget = macro.WindowTarget with { Process = "excel" };
            await Task.Delay(600);
            service.Look();

            Assert.False(Assert.Single(service.Watches()).Matched);
            return true;
        });
    }

    [Fact]
    public void Only_the_triggers_something_looks_at_are_listed()
    {
        Ui.Run(() =>
        {
            var waiting = new MacroItem
            {
                Name = "等窗口",
                TriggerMode = MacroTrigger.Window,
                WindowTarget = new WindowRule(Title: "鲸灵等不到的这个窗口标题"),
            };
            waiting.Steps.Add(new MacroStep { Type = "control.stop" });

            // A key binding answers a key rather than being waited for, and a macro that is switched
            // off is not waiting for anything: neither has a moment it was last looked at.
            var bound = new MacroItem { Name = "按键的", TriggerMode = MacroTrigger.KeystrokesButtonInputs };
            bound.Steps.Add(new MacroStep { Type = "control.stop" });

            var off = new MacroItem
            {
                Name = "关掉的",
                IsEnabled = false,
                TriggerMode = MacroTrigger.Window,
                WindowTarget = new WindowRule(Title: "鲸灵等不到的这个窗口标题"),
            };
            off.Steps.Add(new MacroStep { Type = "control.stop" });

            using var service = new MacroTriggerService(
                () => [waiting, bound, off], new WindowsDeviceLayer(), new NoWindowEvents());

            service.Look();

            var watch = Assert.Single(service.Watches());
            Assert.Equal("等窗口", watch.Macro);
        });
    }

    [Fact]
    public void A_program_that_is_already_running_is_not_something_that_started()
    {
        Ui.Run(() =>
        {
            var mine = Path.GetFileNameWithoutExtension(Environment.ProcessPath!);
            var macro = new MacroItem
            {
                Name = "等自己这个程序",
                TriggerMode = MacroTrigger.Process,
                ProcessName = mine,
                ProcessChange = ProcessChangeKind.Started,
            };
            macro.Steps.Add(new MacroStep { Type = "control.stop" });

            using var service = new MacroTriggerService(
                () => [macro], new WindowsDeviceLayer(), new NoWindowEvents());

            // The reading before this one is where counting starts, so what is running now is what
            // was running then, not something that has just started.
            service.LookAtPrograms();
            var first = Assert.Single(service.Watches());
            Assert.True(first.Matched);
            Assert.False(first.Told);       // a program starting is found out by looking, not by being told
            Assert.True(first.Next > first.Checked);

            service.LookAtPrograms();
            Assert.True(Assert.Single(service.Watches()).Checked >= first.Checked);
        });
    }

}
