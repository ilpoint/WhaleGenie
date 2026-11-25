namespace WhaleGenie.Contracts;

/// <summary>
/// The system switch as the side bar draws it: whether triggers are listening, and the key that
/// flips that switch. The hotkey is display text because the program, not the page, decides how a
/// combination is spelled and what "no key" reads like.
/// </summary>
[BridgeContract]
public sealed record SystemSwitchState(bool Enabled, string HotkeyDisplay);
