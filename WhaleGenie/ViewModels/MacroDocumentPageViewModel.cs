using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Localization;

namespace WhaleGenie.ViewModels;

/// <summary>One step document shown in the editor: the parent macro or one of its children.</summary>
public partial class MacroDocumentPageViewModel : ViewModelBase
{
    public MacroDocumentPageViewModel(MacroEditorViewModel editor, bool isCurrent,
        Models.MacroItem? source = null)
    {
        Editor = editor;
        IsCurrent = isCurrent;
        Source = source;
        editor.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MacroEditorViewModel.Name))
            {
                OnPropertyChanged(nameof(Title));
            }
        };
    }

    public MacroEditorViewModel Editor { get; }

    public bool IsCurrent { get; }

    /// <summary>The definition this page was opened from, or null on the current macro page.</summary>
    public Models.MacroItem? Source { get; }

    public string Title => IsCurrent
        ? Strings.Get("Editor.CurrentMacroPage")
        : Editor.Name;

    [ObservableProperty]
    public partial bool HasProblem { get; set; }

    public override void RefreshLanguage()
    {
        base.RefreshLanguage();
        OnPropertyChanged(nameof(Title));
    }
}
