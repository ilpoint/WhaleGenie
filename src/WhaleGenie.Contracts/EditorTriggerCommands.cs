using System.Text.Json.Serialization;

namespace WhaleGenie.Contracts;

/// <summary>One edit of a trigger entry. The discriminator is the command's kind.</summary>
[BridgeContract]
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(EditorTriggerAddCommand), "add")]
[JsonDerivedType(typeof(EditorTriggerRemoveCommand), "remove")]
[JsonDerivedType(typeof(EditorTriggerBindingCommand), "binding")]
[JsonDerivedType(typeof(EditorTriggerLoopCommand), "loop")]
[JsonDerivedType(typeof(EditorTriggerOnceCommand), "once")]
[JsonDerivedType(typeof(EditorTriggerEnabledCommand), "enabled")]
[JsonDerivedType(typeof(EditorTriggerFieldCommand), "field")]
[JsonDerivedType(typeof(EditorTriggerHotstringCommand), "hotstring")]
[JsonDerivedType(typeof(EditorTriggerHotstringEndKeyCommand), "hotstringEndKey")]
[JsonDerivedType(typeof(EditorTriggerHotstringEndResetCommand), "hotstringEndReset")]
[JsonDerivedType(typeof(EditorTriggerHotstringEndCaptureCommand), "hotstringEndCapture")]
[JsonDerivedType(typeof(EditorTriggerInterfaceHotkeyCommand), "interfaceHotkey")]
[JsonDerivedType(typeof(EditorTriggerInterfaceHotkeyClearCommand), "interfaceHotkeyClear")]
[JsonDerivedType(typeof(EditorTriggerIdleCommand), "idle")]
public abstract record EditorTriggerCommand;

/// <summary>Add one trigger entry of the named kind.</summary>
[BridgeContract]
public sealed record EditorTriggerAddCommand(string TriggerKind) : EditorTriggerCommand;

/// <summary>Remove one trigger entry.</summary>
[BridgeContract]
public sealed record EditorTriggerRemoveCommand(string TriggerId) : EditorTriggerCommand;

/// <summary>Bind the trigger to a key.</summary>
[BridgeContract]
public sealed record EditorTriggerBindingCommand(string TriggerId, string Binding) : EditorTriggerCommand;

/// <summary>How the trigger repeats.</summary>
[BridgeContract]
public sealed record EditorTriggerLoopCommand(string TriggerId, string Loop) : EditorTriggerCommand;

/// <summary>Whether the trigger only fires once per session.</summary>
[BridgeContract]
public sealed record EditorTriggerOnceCommand(string TriggerId, bool Once) : EditorTriggerCommand;

/// <summary>Whether the trigger entry is enabled.</summary>
[BridgeContract]
public sealed record EditorTriggerEnabledCommand(string TriggerId, bool Enabled) : EditorTriggerCommand;

/// <summary>
/// One field of a trigger whose fields are read by family: timer, file, process, hotstring option,
/// colour, window or interface.
/// </summary>
[BridgeContract]
public sealed record EditorTriggerFieldCommand(
    string TriggerId, string Family, string Field, string Value) : EditorTriggerCommand;

/// <summary>The typed text of a hotstring trigger.</summary>
[BridgeContract]
public sealed record EditorTriggerHotstringCommand(string TriggerId, string Value) : EditorTriggerCommand;

/// <summary>One end key of a hotstring trigger.</summary>
[BridgeContract]
public sealed record EditorTriggerHotstringEndKeyCommand(
    string TriggerId, string Value, bool Checked) : EditorTriggerCommand;

/// <summary>Reset the end keys to their defaults.</summary>
[BridgeContract]
public sealed record EditorTriggerHotstringEndResetCommand(string TriggerId) : EditorTriggerCommand;

/// <summary>Capture the next typed character as an end key.</summary>
[BridgeContract]
public sealed record EditorTriggerHotstringEndCaptureCommand(
    string TriggerId, string Character) : EditorTriggerCommand;

/// <summary>The hotkey the interface trigger listens for.</summary>
[BridgeContract]
public sealed record EditorTriggerInterfaceHotkeyCommand(
    string TriggerId, string Binding) : EditorTriggerCommand;

/// <summary>Clear the interface trigger's hotkey.</summary>
[BridgeContract]
public sealed record EditorTriggerInterfaceHotkeyClearCommand(string TriggerId) : EditorTriggerCommand;

/// <summary>How long the machine must be idle before an idle trigger fires.</summary>
[BridgeContract]
public sealed record EditorTriggerIdleCommand(string TriggerId, string Value) : EditorTriggerCommand;
