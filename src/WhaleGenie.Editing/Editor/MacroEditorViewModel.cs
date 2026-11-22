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
using WhaleGenie.Editing;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WindowRuleModel = WhaleGenie.Core.Devices.WindowRule;

namespace WhaleGenie.ViewModels;

public sealed record TriggerKindChoice(TriggerKind Kind, string Label);

/// <summary>
/// The two halves of the macro settings drawer. They are pages rather than one long column
/// because the list of trigger entries and the macro's own interface each need the room.
/// </summary>
public enum MacroSettingsPage
{
    /// <summary>What the macro is called and how it starts.</summary>
    General,

    /// <summary>What belongs to this macro alone: its limit, its switch key and its interface.</summary>
    Specific,
}

public partial class MacroEditorViewModel : ViewModelBase
{
    /// <summary>The values kept on this machine, shared with the project that opened this editor.</summary>
    public PersistedVariables Persisted { get; init; } = new();

    /// <summary>Machine settings the editor reads, kept behind a port so the model stays UI-free.</summary>
    public IEditorSettings Settings { get; init; } = DefaultEditorSettings.Instance;

    /// <summary>
    /// The project's shared values, shared with every run and every other editor window. An editor
    /// built on its own — a check, a page that only reads the palette — gets a session of its own.
    /// </summary>
    public VariableSession Variables { get; init; } = new();

    /// <summary>Raised when the editor should close, with the macro to save or <c>null</c> when cancelled.</summary>
    public event Action<MacroDraft?>? CloseRequested;

    /// <summary>Action types offered by the Action Editor palette.</summary>
    public IReadOnlyList<MacroAction> ActionPalette { get; } =
    [
        new() { LabelKey = "Palette.Undo", Key = "undo", NeedsUndo = true },

        new() { LabelKey = "Palette.Redo", Key = "redo", NeedsRedo = true },

        new() { LabelKey = "Palette.Edit", Key = "edit", NeedsSingleSelection = true },

        new() { LabelKey = "Palette.ToggleEnabled", Key = "toggleEnabled", NeedsSelection = true },

        // Copy borrows the two-sheets glyph: with the duplicate entry gone there is one copy
        // button, and this reads as "a copy of the steps" more plainly than a single sheet.
        new() { LabelKey = "Palette.Copy", Key = "copy", NeedsSelection = true },

        new() { LabelKey = "Palette.Cut", Key = "cut", NeedsSelection = true },

        // A plain clipboard, the usual "paste" glyph. The old one was a clipboard with a
        // downward arrow, which reads as "download" and had nothing to do with the step list.
        new() { LabelKey = "Palette.Paste", Key = "paste" },

        new() { LabelKey = "Palette.Delete", Key = "delete", NeedsSelection = true },

        new() { LabelKey = "Palette.Group",
                Key = "group", NeedsSelection = true },

        // An arrow leaving a box: the selected steps are taken out of this macro and put
        // into one of their own, with a call left where they were.
        new() { LabelKey = "Palette.LiftToMacro",
                Key = "liftToMacro", NeedsSelection = true },

        new() { LabelKey = "Palette.MoveUp", Key = "moveUp", NeedsSelection = true },

        new() { LabelKey = "Palette.MoveDown", Key = "moveDown", NeedsSelection = true },

        new() { LabelKey = "Palette.Clear", Key = "clear", NeedsSteps = true },

        new() { LabelKey = "Palette.AdjustDelays", Key = "adjustDelays",
                NeedsSteps = true },

        new() { LabelKey = "Palette.RunMacro", Key = "runMacro" },
    ];

    private readonly List<MacroStep> _selection = [];

    /// <summary>
    /// The exact list and index an action chosen from the library will occupy. The owner and
    /// parameter travel with it because a section can limit which action kinds it accepts.
    /// </summary>
    private StepInsertionTarget? _insertAt;

    private sealed record StepInsertionTarget(IList<MacroStep> List, int Index,
        MacroStep? Owner, StepParameter? Parameter);

    private bool _loading;

    /// <summary>Where a captured key goes when the inspector asked for one, or null for the trigger.</summary>
    private Action<string>? _keyCaptureTarget;

    private bool _captureKeyboardOnly;
    private bool _captureSingleKey;
    private TriggerEntry? _selectedTrigger;
    private bool _updatingHotstringEndKeys;
    private readonly Dictionary<string, InterfaceLayout> _interfaceLayouts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<InterfaceGroupMember>> _interfaceMembers =
        new(StringComparer.Ordinal);

    /// <summary>How many step-list changes can be undone.</summary>
    private const int HistoryLimit = 60;

    private readonly List<HistoryEntry> _undo = [];
    private readonly List<HistoryEntry> _redo = [];

    /// <summary>
    /// The steps and the selection as they were before one change. The selection is kept as the
    /// positions the steps are written in, counting nested ones in the order the list shows them,
    /// because the history holds copies of the steps rather than the steps themselves.
    /// </summary>
    private sealed record HistoryEntry(
        IReadOnlyList<MacroStep> Steps, IReadOnlyList<int> Selection, double DelayScale);
}
