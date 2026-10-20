using CommunityToolkit.Mvvm.ComponentModel;

namespace WhaleGenie.ViewModels;

/// <summary>One checkbox in the ending-key set; custom entries are captured from the keyboard.</summary>
public partial class HotstringEndKeyOption(
    string value, string label, bool isCustom) : ObservableObject
{
    public string Value { get; } = value;

    public string Label { get; } = label;

    public bool IsCustom { get; } = isCustom;

    [ObservableProperty]
    public partial bool IsChecked { get; set; }
}
