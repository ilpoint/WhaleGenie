using WhaleGenie.Core.Devices;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The pretend controller: every press, stick and trigger written down, so a check can see what the
/// steps asked the pad to do.
/// </summary>
internal sealed class FakeGamepad(FakeDesk desk) : IGamepadDevice
{
    public void Connect() => desk.Note("gamepadConnect");

    public void Button(string button, bool down) => desk.Note($"gamepadButton {button} {down}");

    public void Stick(string stick, int x, int y) => desk.Note($"gamepadStick {stick} {x} {y}");

    public void Trigger(string trigger, int amount) => desk.Note($"gamepadTrigger {trigger} {amount}");

    public void ReleaseAll() => desk.Note("gamepadRelease");
}
