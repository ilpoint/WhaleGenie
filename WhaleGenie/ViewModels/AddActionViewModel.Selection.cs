using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;

namespace WhaleGenie.ViewModels;

// What the form is editing: the action chosen, the devices it reads, the page shown and the global draft.
public partial class AddActionViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Header))]
    [NotifyPropertyChangedFor(nameof(CommitLabel))]
    public partial bool IsEditing { get; set; }

    /// <summary>
    /// The name the step goes by — the one it already has, or the one it is about to be given.
    /// </summary>
    public string StepId => _editingId;

    /// <summary>True while there is a name to show.</summary>
    public bool HasStepId => _editingId.Length > 0;

    /// <summary>
    /// The windows on the desktop, so a position picked off the screen can be stored the way a
    /// window-anchored step reads it back. The dialog fills this in; without it a picked position
    /// is written down as screen pixels, which is what every unanchored step holds anyway.
    /// </summary>
    public IWindowDevice? Windows { get; set; }

    /// <summary>
    /// The clipboard, so a step whose question is what the machine is holding right now can show it
    /// while it is being written. Filled in by the dialog; without it the preview says nothing
    /// rather than failing, which is what a form opened with no devices behind it has to do.
    /// </summary>
    public IClipboardDevice? Clipboard
    {
        get => _clipboard;
        set
        {
            _clipboard = value;
            RefreshClipboardPreview();
        }
    }

    private IClipboardDevice? _clipboard;

    /// <summary>
    /// The controls on the desktop, so a position picked off the screen can be stored the way a
    /// step anchored to a control reads it back. Filled in by the dialog beside
    /// <see cref="Windows"/>, and unused by a step that measures from anything else.
    /// </summary>
    public IUiDevice? Ui { get; set; }

    /// <summary>Title-bar text, which changes when an existing step is being edited.</summary>
    public string Header => Strings.Get(IsEditing ? "Add.EditTitle" : "Add.Title");

    /// <summary>Label of the confirm button.</summary>
    public string CommitLabel => Strings.Get(IsEditing ? "Add.CommitEdit" : "Add.Commit");

    /// <summary>Label of the button that ends a step.</summary>
    public string CancelLabel => Strings.Get("Add.Cancel");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(SelectedKey))]
    [NotifyPropertyChangedFor(nameof(Description))]
    [NotifyPropertyChangedFor(nameof(SelectedActionName))]
    [NotifyPropertyChangedFor(nameof(IsBlock))]
    [NotifyPropertyChangedFor(nameof(ShowsRun))]
    [NotifyPropertyChangedFor(nameof(CanRun))]
    [NotifyPropertyChangedFor(nameof(ShowsClipboardPreview))]
    public partial ActionDefinition? SelectedDefinition { get; set; }

    private VariableDefinitionDraft? _globalVariableDraft;

    /// <summary>The global declaration edited together with a Set Variable step.</summary>
    public VariableDefinitionDraft? GlobalVariableDraft
    {
        get => _globalVariableDraft;
        private set
        {
            if (ReferenceEquals(_globalVariableDraft, value))
            {
                return;
            }

            if (_globalVariableDraft is not null)
            {
                _globalVariableDraft.PropertyChanged -= OnGlobalDraftChanged;
            }

            _globalVariableDraft = value;
            if (_globalVariableDraft is not null)
            {
                _globalVariableDraft.PropertyChanged += OnGlobalDraftChanged;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowsGlobalPersistence));
            OnPropertyChanged(nameof(CanSave));
            OnPropertyChanged(nameof(CanRun));
            OnPropertyChanged(nameof(ValidationMessage));
        }
    }

    public bool ShowsGlobalPersistence => GlobalVariableDraft is not null;

    public bool HasGlobalVariableProblem => GlobalVariableProblem.Length > 0;

    public string GlobalVariableProblem
    {
        get
        {
            if (GlobalVariableDraft is not { } draft)
            {
                return string.Empty;
            }

            var name = draft.Name.Trim();
            if (!VariableText.IsName(name))
            {
                return Strings.Format("GlobalVariables.BadName", name);
            }

            if (GlobalVariablesViewModel.Reserved(name))
            {
                return Strings.Format("GlobalVariables.ReservedName", name);
            }

            return draft.DefaultHasError
                ? Strings.Format("GlobalVariables.BadDefault", name, draft.DefaultPreview)
                : string.Empty;
        }
    }

    /// <summary>
    /// True when the step being edited holds steps of its own. The settings every step carries
    /// then read as being about the whole block: a failure inside it that no step handled is this
    /// block failing, and the block is what gets retried or left out.
    /// </summary>
    public bool IsBlock => SelectedDefinition is { } definition && HoldsSteps(definition);

    /// <summary>Editors for the selected action, rebuilt whenever the selection changes.</summary>
    public ObservableCollection<StepParameterViewModel> Parameters { get; } = [];

    /// <summary>
    /// The four pages a step is edited on, in the order they are read. A step of a dozen fields is
    /// the reason they exist: which page a field belongs on is decided by the catalogued parameter
    /// rather than here, and this only says which page is open.
    /// </summary>
    public ObservableCollection<ActionPageViewModel> Pages { get; } = [];

    /// <summary>Key of the page that holds what the step is: what it looks for, where, how soon.</summary>
    public const string BasePage = "base";

    /// <summary>
    /// Key of the page that holds what every step has whatever the action is: how long it may take,
    /// how often it is retried, and what the macro does when it fails. It sits right after the page
    /// about the action itself because it is the next question people ask about a step.
    /// </summary>
    public const string ExecPage = "exec";

    /// <summary>Key of the page that holds the settings only a different approach needs.</summary>
    public const string AdvancedPage = "advanced";

    /// <summary>Key of the page that holds the results this step may hand over.</summary>
    public const string ReturnsPage = "returns";

    /// <summary>Key of the page that holds the settings every step has, and the JSON it comes to.</summary>
    public const string OtherPage = "other";
}
