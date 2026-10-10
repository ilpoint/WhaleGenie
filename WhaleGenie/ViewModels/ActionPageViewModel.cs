using CommunityToolkit.Mvvm.ComponentModel;

namespace WhaleGenie.ViewModels;

/// <summary>
/// One page of the "Add Action" dialog: a part of a step's settings that belongs together. A form
/// of a dozen fields is read a page at a time rather than as one long wall, and which page a field
/// belongs on is a question about the field: what the step is, how it does it, what it leaves
/// behind, or the settings every step has.
/// </summary>
public partial class ActionPageViewModel : ViewModelBase
{
    /// <summary>Stable name of this page, which is how the dialog asks which one is open.</summary>
    public required string Key { get; init; }

    /// <summary>Name shown on the tab, in the interface language.</summary>
    public required string Title { get; init; }

    /// <summary>True while this is the page on screen.</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    /// <summary>
    /// True when this page holds something that is not the value it starts on. The tab then carries
    /// a mark, so a step whose behaviour differs from the ordinary is not edited blind: what was
    /// changed may be on a page nobody has looked at yet.
    /// </summary>
    [ObservableProperty]
    public partial bool HasDot { get; set; }

    /// <summary>True when this page has anything to show at all.</summary>
    [ObservableProperty]
    public partial bool IsPresent { get; set; } = true;
}
