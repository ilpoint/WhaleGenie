using WhaleGenie.Localization;

namespace WhaleGenie.Editing;

/// <summary>The settings file behind the editor model's port.</summary>
public sealed class LocalEditorSettings : IEditorSettings
{
    public string RecordStopKey => LocalSettings.LoadRecordStopKey();

    public int InputRate => LocalSettings.LoadInputRate();

    public string MenuLauncher => LocalSettings.LoadMenuLauncher();

    public string SystemHotkey => LocalSettings.LoadSystemHotkey();
}
