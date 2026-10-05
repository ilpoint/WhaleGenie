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
    }

    public IReadOnlyList<LanguageOption> Languages { get; } =
    [
        new(Language.English, "English"),
        new(Language.Chinese, "中文"),
    ];

    [ObservableProperty]
    public partial LanguageOption SelectedLanguage { get; set; }

    partial void OnSelectedLanguageChanged(LanguageOption value)
        => Strings.Current.Language = value.Language;
}
