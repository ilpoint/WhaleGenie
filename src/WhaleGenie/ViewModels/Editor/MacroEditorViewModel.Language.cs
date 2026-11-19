using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WindowRuleModel = WhaleGenie.Core.Devices.WindowRule;

namespace WhaleGenie.ViewModels;

// Re-reading the labels this editor computes when the language changes.
public partial class MacroEditorViewModel
{
    /// <summary>Re-reads the labels this editor computes, the palette's tips and the step rows.</summary>
    public override void RefreshLanguage()
    {
        base.RefreshLanguage();
        Notice = string.Empty;
        foreach (var action in ActionPalette)
        {
            action.RefreshLanguage();
        }

        foreach (var row in Rows)
        {
            row.RefreshLanguage();
        }

        WindowPreview.RefreshLanguage();
        OnPropertyChanged(nameof(ToggleKeyDisplay));
        OnPropertyChanged(nameof(ToggleKeyButton));
    }

    /// <summary>Clears changes made only by controls taking their initial bound values.</summary>
    internal void AcceptInitialBindings() => IsDirty = false;

    /// <summary>Raised when the editor should open the "Add Action" dialog.</summary>
    public event Action? AddStepRequested;

    /// <summary>Raised when the editor should open the edit dialog for an existing step.</summary>
    public event Action<MacroStep>? EditStepRequested;

    /// <summary>Raised when the palette asks to clear the list, so the window can confirm it.</summary>
    public event Action? ClearRequested;

    /// <summary>Raised when the palette or a shortcut asks to copy the selected steps.</summary>
    public event Action? CopyRequested;

    /// <summary>Raised when the palette or a shortcut asks to cut the selected steps.</summary>
    public event Action? CutRequested;

    /// <summary>Raised when the palette or a shortcut asks to paste below the selection.</summary>
    public event Action? PasteRequested;

    /// <summary>Raised when the editor should open the run window for the current steps.</summary>
    public event Action? RunRequested;

    /// <summary>
    /// Raised when a save was asked for while the name or a declaration still blocks it. The window
    /// answers by showing the page where the reason is written.
    /// </summary>
    public event Action? SaveBlocked;

    /// <summary>Raised when the palette asks to open the run-speed dialog.</summary>
    public event Action? AdjustDelaysRequested;

    /// <summary>
    /// Raised when a stretch of steps was lifted into a macro of its own: the project takes the
    /// new macro, because the call left behind in this one is a call to it.
    /// </summary>
    public event Action<MacroItem>? MacroLifted;

    /// <summary>Raised when the palette asks for a step that runs another macro.</summary>
    public event Action? RunMacroRequested;

    /// <summary>
    /// Raised when recording starts or stops, so the window can put the input hook up or take
    /// it down. <c>true</c> means a recording is beginning.
    /// </summary>
    public event Action<bool>? RecordingChanged;

    /// <summary>
    /// Raised when the list should re-apply the current selection, for example after
    /// a move or a duplicate, where the items change identity or position.
    /// </summary>
    public event Action? SelectionRefreshRequested;

    /// <summary>Steps built so far; saved as the macro's JSON tree.</summary>
    public ObservableCollection<MacroStep> Steps { get; } = [];

    /// <summary>
    /// The steps as the editor shows them: one node per step, and the steps a block runs hanging
    /// under it. This is the shape the tree control draws, so folding is the control's own
    /// business rather than something the editor has to rebuild for.
    /// </summary>
    public ObservableCollection<StepRow> Roots { get; } = [];

    /// <summary>
    /// The same tree read in document order, each node once. Commands work on steps rather than
    /// rows, so most of what the editor does needs the depth and the list a row belongs to, and a
    /// flat walk of the tree is the shortest way to ask for it.
    /// </summary>
    public ObservableCollection<StepRow> Rows { get; } = [];

    public bool HasSteps => Steps.Count > 0;

    /// <summary>The document tabs shared by the parent and every child editor in one window.</summary>
    public ObservableCollection<MacroDocumentPageViewModel> Documents { get; internal set; } = [];
}
