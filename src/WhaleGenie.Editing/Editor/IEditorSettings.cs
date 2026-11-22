namespace WhaleGenie.Editing;

/// <summary>The machine settings the editor model reads; the app layer owns the settings file.</summary>
public interface IEditorSettings
{
    string RecordStopKey { get; }

    int InputRate { get; }

    string MenuLauncher { get; }

    string SystemHotkey { get; }
}
