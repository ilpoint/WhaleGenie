using WhaleGenie.Localization;

namespace WhaleGenie.Editing;

/// <summary>The settings file behind the editor model's port.</summary>
public sealed class LocalEditorSettings(AppSettings settings) : IEditorSettings
{
    public string RecordStopKey => settings.LoadRecordStopKey();

    public int InputRate => settings.LoadInputRate();

    public string MenuLauncher => settings.LoadMenuLauncher();

    public string SystemHotkey => settings.LoadSystemHotkey();
}
