using WhaleGenie.Core.Devices;

namespace WhaleGenie.Editing;

/// <summary>The values a model built on its own — a check or a page that only reads — starts with.</summary>
public sealed class DefaultEditorSettings : IEditorSettings
{
    public static DefaultEditorSettings Instance { get; } = new();

    public string RecordStopKey => "Ctrl+F10";

    public int InputRate => InputLimit.Usual;

    public string MenuLauncher => string.Empty;

    public string SystemHotkey => "Home";
}
