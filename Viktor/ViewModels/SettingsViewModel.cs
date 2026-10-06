using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Viktor.Localization;

namespace Viktor.ViewModels;

/// <summary>One entry of the language picker, named in its own language.</summary>
public sealed record LanguageOption(Language Language, string Display)
{
    public override string ToString() => Display;
}

/// <summary>Backs the settings window. Changing the language applies immediately.</summary>
public partial class SettingsViewModel : ViewModelBase
{
    public SettingsViewModel()
    {
        SelectedLanguage = Languages.First(option => option.Language == Strings.Current.Language);
        FailureScreenshot = LocalSettings.LoadFailureScreenshot();
    }

    public IReadOnlyList<LanguageOption> Languages { get; } =
    [
        new(Language.English, "English"),
        new(Language.Chinese, "中文"),
    ];

    [ObservableProperty]
    public partial LanguageOption SelectedLanguage { get; set; }

    /// <summary>
    /// Whether a run that stops on a failure leaves a picture of the screen in the log folder.
    /// </summary>
    [ObservableProperty]
    public partial bool FailureScreenshot { get; set; }

    partial void OnSelectedLanguageChanged(LanguageOption value)
        => Strings.Current.Language = value.Language;

    /// <summary>
    /// Writes the choice down, but only when it really changed: opening the window reads the
    /// stored value into this property, and that must not count as the user asking for something.
    /// </summary>
    partial void OnFailureScreenshotChanged(bool value)
    {
        if (value != LocalSettings.LoadFailureScreenshot())
        {
            LocalSettings.StoreFailureScreenshot(value);
        }
    }
}
