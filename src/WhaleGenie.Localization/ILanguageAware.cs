namespace WhaleGenie.Localization;

/// <summary>
/// An object whose text comes from the localization table. A language change asks it to re-read
/// everything it shows, so labels computed in code refresh in place instead of waiting for the
/// window to be opened again.
/// </summary>
public interface ILanguageAware
{
    void RefreshLanguage();
}
