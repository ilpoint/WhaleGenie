using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;
using WhaleGenie.Runtime;

namespace WhaleGenie.Tests;

/// <summary>
/// The trigger host without a window: the hooks it mounts, the system switch it follows, the
/// sounds it plays, the plan it publishes and the questions it hands to the run. The surfaces are
/// stand-ins that record what the host asked for, so the host's own reactions are asserted without
/// a screen and without a web control.
/// </summary>
public class TriggerHostTests : IDisposable
{
    private readonly SystemState _system = new(false);
    private readonly ProjectWorkspace _workspace = new();
    private readonly TriggerRuntime _runtime = new(NullDeviceLayer.Instance, new VariableSession());
    private readonly RecordingSurface _surface = new();
    private readonly RecordingQuestions _questions = new();
    private readonly FocusProbe _focus = new();
    private readonly List<UiSound> _sounds = [];

    private TriggerHost Host { get; }

    public TriggerHostTests()
    {
        Host = new TriggerHost(_workspace, _system, _runtime, NullDeviceLayer.Instance,
            _surface, _questions, _focus, sound => _sounds.Add(sound));
        Host.Attach();
    }

    public void Dispose()
    {
        Host.Dispose();
        _runtime.Dispose();
    }

    [Fact]
    public void Attaching_follows_the_system_switch()
    {
        Assert.False(_runtime.Triggers.IsEnabled);

        _system.Set(true);
        Assert.True(_runtime.Triggers.IsEnabled);
        Assert.Equal([UiSound.SystemOn], _sounds);
        Assert.True(_surface.RefreshCalled);

        _system.Set(false);
        Assert.False(_runtime.Triggers.IsEnabled);
        Assert.Equal([UiSound.SystemOn, UiSound.SystemOff], _sounds);
        Assert.True(_surface.CloseMenuCalled);
    }

    [Fact]
    public void The_plan_carries_the_system_key_and_the_armed_macros()
    {
        _system.Set(true);
        var macro = new MacroItem { Name = "测试宏", ToggleKey = "F8" };
        _workspace.AddMacro(macro);
        _workspace.Triggers.Entries.Add(new TriggerEntry
        {
            MacroName = macro.Name,
            Spec = new KeySpec("F9"),
        });

        var plan = Host.Plan;
        Assert.Equal("F8", plan.MacroToggles.Single().Binding);
        Assert.Contains("F9", plan.ClaimedBindings);
        Assert.True(plan.AutomationEnabled);
    }

    [Fact]
    public void A_hidden_hook_does_not_stop_the_switch_from_working()
    {
        // NullDeviceLayer refuses subscriptions, which is the machine whose hooks cannot mount.
        // The host still follows the switch and still publishes the plan.
        _system.Set(true);
        Assert.True(_runtime.Triggers.IsEnabled);
        Assert.True(Host.Plan.AutomationEnabled);
    }

    [Fact]
    public async Task The_run_questions_are_the_hosts_questions()
    {
        Assert.NotNull(_runtime.Triggers.Ask);
        Assert.NotNull(_runtime.Triggers.AskUser);
        Assert.NotNull(_runtime.Triggers.ConfirmIrreversible);

        var answer = await _runtime.Triggers.Ask!("step", "Run.Failed", "detail", "note");
        Assert.Equal(StepErrorChoice.Retry, answer);
        Assert.True(_questions.Asked);
    }

    [Fact]
    public void Disposing_takes_the_questions_and_the_triggers_with_it()
    {
        _system.Set(true);
        Host.Dispose();

        Assert.False(_runtime.Triggers.IsEnabled);
        Assert.Null(_runtime.Triggers.Ask);
        Assert.Null(_runtime.Triggers.AskUser);
        Assert.Null(_runtime.Triggers.ConfirmIrreversible);
        Assert.Same(GlobalHotkeyPlan.Empty, Host.Plan);

        // A second dispose is a no-op, not a second teardown.
        Host.Dispose();
    }

    [Fact]
    public void The_focus_probe_answers_what_the_window_copied()
    {
        Assert.False(_focus.IsTextInputFocused);
        _focus.IsTextInputFocused = true;
        Assert.True(_focus.IsTextInputFocused);
    }

    private sealed class RecordingSurface : ITriggerSurfaceHost
    {
        public bool MenuOpen { get; set; }
        public bool MenuSearchFocused { get; set; }
        public bool MenuPinned { get; set; }
        public bool RefreshCalled { get; private set; }
        public bool CloseMenuCalled { get; private set; }

        public void CloseMenuFromLauncher() => CloseMenuCalled = true;
        public void OpenMenu() { }
        public void MenuHandleKey(string name) { }
        public void MenuMouseDown(MouseHookEvent e) { }
        public void WheelMouseDown(MouseHookEvent e) { }
        public void WheelMouseMoved(MouseMovedHookEvent e) { }
        public void WheelMouseUp(MouseHookEvent e) { }
        public void WheelKeyDown(KeyHookEvent e) { }
        public void WheelScroll(WheelHookEvent e) { }
        public void ToggleInterfaceGroup(string groupKey) { }

        public void RefreshForSystemToggle() => RefreshCalled = true;
    }

    private sealed class RecordingQuestions : IRunQuestionHost
    {
        public bool Asked { get; private set; }

        public Task<StepErrorChoice> AskAboutFailedStep(string step, string reason, string detail,
            string comment)
        {
            Asked = true;
            return Task.FromResult(StepErrorChoice.Retry);
        }

        public Task<AskAnswer> AskUser(AskQuestion question)
            => Task.FromResult(new AskAnswer(false, string.Empty));

        public Task<bool> ConfirmIrreversibleStep(ExecutableStep step)
            => Task.FromResult(false);
    }

    private sealed class FocusProbe : ITriggerFocusProbe
    {
        public bool IsTextInputFocused { get; set; }
    }
}
