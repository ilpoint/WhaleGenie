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

            if (args.PropertyName == nameof(MacroEditorViewModel.IsDirty))
            {
                OnPropertyChanged(nameof(IsUnsaved));
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

    /// <summary>
    /// True on the page in front while the macro has changes that are not saved. A tab that keeps
    /// nothing else about being written, so the dot is the only sign until it is saved; the two
    /// other pages of a child macro are the same document, so they carry it too through the same
    /// editor. Shown as a shape rather than a colour alone, and with a tip, so it reads without
    /// having to know the colour's meaning.
    /// </summary>
    public bool IsUnsaved => Editor.IsDirty && IsCurrent;

    [ObservableProperty]
    public partial bool HasProblem { get; set; }

    public override void RefreshLanguage()
    {
        base.RefreshLanguage();
        OnPropertyChanged(nameof(Title));
    }
}
