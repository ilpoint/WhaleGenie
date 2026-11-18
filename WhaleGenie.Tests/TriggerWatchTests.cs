using System.IO;
using WhaleGenie.Core.Devices;
using WhaleGenie.Models;
using WhaleGenie.Platform.Windows;
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
            var macro = new MacroItem { Name = "等一个不存在的窗口" };
            macro.Steps.Add(new MacroStep { Type = "control.stop" });
            var spec = new WindowSpec(new WindowRule(Title: "鲸灵等不到的这个窗口标题"));

            using var service = new MacroTriggerService(
                new WindowsDeviceLayer(), new VariableSession(), new NoWindowEvents());
            service.Publish(TriggerPlans.For(macro, spec));

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

            var macro = new MacroItem { Name = "等这个程序" };
            macro.Steps.Add(new MacroStep { Type = "control.stop" });

            using var service = new MacroTriggerService(
                new ChecksDevices { Windows = windows }, new VariableSession(), new NoWindowEvents());
            service.Publish(TriggerPlans.For(macro,
                new WindowSpec(new WindowRule(Process: "notepad", How: WindowHow.Process))));

            service.Look();

            // The window the rule names is open, which is what the reading says: nothing here is
            // about the title, because the rule is not either.
            Assert.True(Assert.Single(service.Watches()).Matched);

            // A rule nothing answers to waits, rather than being taken for the window it is not.
            // The window list is looked at every half second, which is what the wait is for.
            service.Publish(TriggerPlans.For(macro,
                new WindowSpec(new WindowRule(Process: "excel", How: WindowHow.Process))));
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
            var waiting = new MacroItem { Name = "等窗口" };
            waiting.Steps.Add(new MacroStep { Type = "control.stop" });

            // A key binding answers a key rather than being waited for, and a macro that is switched
            // off is not waiting for anything: neither has a moment it was last looked at.
            var bound = new MacroItem { Name = "按键的" };
            bound.Steps.Add(new MacroStep { Type = "control.stop" });

            var off = new MacroItem { Name = "关掉的", IsEnabled = false };
            off.Steps.Add(new MacroStep { Type = "control.stop" });

            using var service = new MacroTriggerService(
                new WindowsDeviceLayer(), new VariableSession(), new NoWindowEvents());
            service.Publish(TriggerPlans.Build()
                .Macro(waiting, new WindowSpec(new WindowRule(Title: "鲸灵等不到的这个窗口标题")))
                .Macro(bound, new KeySpec("F8"))
                .Macro(off, new WindowSpec(new WindowRule(Title: "鲸灵等不到的这个窗口标题")))
                .Build());

            service.Look();

            // The key row is listed too, because it is something the runtime is waiting for; the
            // switched-off macro is not.
            Assert.Equal(2, service.Watches().Count);
            Assert.Contains(service.Watches(), watch => watch.Macro == "等窗口");
            Assert.DoesNotContain(service.Watches(), watch => watch.Macro == "关掉的");
        });
    }

    [Fact]
    public void A_program_that_is_already_running_is_not_something_that_started()
    {
        Ui.Run(() =>
        {
            var mine = Path.GetFileNameWithoutExtension(Environment.ProcessPath!);
            var macro = new MacroItem { Name = "等自己这个程序" };
            macro.Steps.Add(new MacroStep { Type = "control.stop" });

            using var service = new MacroTriggerService(
                new WindowsDeviceLayer(), new VariableSession(), new NoWindowEvents());
            service.Publish(TriggerPlans.For(macro,
                new ProcessSpec(mine, ProcessChangeKind.Started)));

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
