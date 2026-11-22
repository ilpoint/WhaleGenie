namespace WhaleGenie.Contracts;

/// <summary>
/// The one theme choice as the bridge sees it. The palette math and applying resources belong to
/// the UI project; the handler only asks for the current state and asks to save a choice.
/// </summary>
public interface IThemeCommands
{
    ThemeState Current();

    /// <summary>Validates, applies and stores one choice; null means the choice was refused.</summary>
    ThemeState? Save(string mode, bool? baseLight, string? accentHex);
}
