using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The pretend machine: what it calls itself, the environment it hands a program, and the settings a
/// step can turn up, down or off.
/// </summary>
internal sealed class FakeSystem(FakeDesk desk) : ISystemDevice
{
    /// <summary>What reading a machine fact answers.</summary>
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What reading an environment variable answers.</summary>
    public Dictionary<string, string> EnvironmentValues { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The layouts the fake says are installed, and which of them is being typed in.</summary>
    public List<string> Layouts { get; } =
        ["\u4e2d\u6587(\u7b80\u4f53) - \u5fae\u8f6f\u62fc\u97f3", "\u82f1\u8bed(\u7f8e\u56fd)"];

    public string Layout { get; set; } = "\u4e2d\u6587(\u7b80\u4f53) - \u5fae\u8f6f\u62fc\u97f3";

    /// <summary>What the speakers are set to in the fake, and whether they are switched off.</summary>
    public int SpeakerVolume { get; set; } = 50;

    public bool SoundOff { get; set; }

    /// <summary>How bright the fake says the screens are.</summary>
    public int ScreenBrightness { get; set; } = 50;

    public string Info(string field)
    {
        desk.Note($"info {field}");
        return Values.TryGetValue(field, out var value) ? value : string.Empty;
    }

    public string Environment(string name)
    {
        desk.Note($"env {name}");
        return EnvironmentValues.TryGetValue(name, out var value) ? value : string.Empty;
    }

    public void Power(PowerAction action, int graceSeconds) => desk.Note($"power {action} {graceSeconds}");

    public int Volume()
    {
        desk.Note("volume");
        return SpeakerVolume;
    }

    public void SetVolume(int percent)
    {
        desk.Note($"setVolume {percent}");
        SpeakerVolume = Math.Clamp(percent, 0, 100);
    }

    public bool IsMuted()
    {
        desk.Note("isMuted");
        return SoundOff;
    }

    public void SetMuted(bool muted)
    {
        desk.Note($"setMuted {muted}");
        SoundOff = muted;
    }

    public void PlaySound(SoundKind kind) => desk.Note($"sound {kind}");

    public void Notify(string title, string text, NotificationKind kind)
        => desk.Note($"notify {kind} [{title}] {text}");

    public string InputMethod()
    {
        desk.Note("inputMethod");
        return Layout;
    }

    public IReadOnlyList<string> InputMethods()
    {
        desk.Note("inputMethods");
        return Layouts;
    }

    public string? SwitchInputMethod(string layout)
    {
        desk.Note($"switchInputMethod {layout}");
        var found = Layouts.FirstOrDefault(installed =>
            installed.Contains(layout, StringComparison.OrdinalIgnoreCase));
        if (found is null)
        {
            return null;
        }

        Layout = found;
        return found;
    }

    public int Brightness()
    {
        desk.Note("brightness");
        return ScreenBrightness;
    }

    public void SetBrightness(int percent)
    {
        desk.Note($"setBrightness {percent}");
        ScreenBrightness = Math.Clamp(percent, 0, 100);
    }
}
