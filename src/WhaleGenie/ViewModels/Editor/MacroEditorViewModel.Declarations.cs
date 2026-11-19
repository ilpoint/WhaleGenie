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

// What the macro says about itself: its documents, origin, name, parameters, results and whether it can be saved.
public partial class MacroEditorViewModel
{
    /// <summary>Child definitions carried by this document when it is read back or saved.</summary>
    public List<MacroItem> SubMacros { get; } = [];

    /// <summary>
    /// True when this editor is showing a callable definition owned by another macro. Such a
    /// definition has no trigger of its own, so trigger-only validation must not block it.
    /// </summary>
    public bool IsSubMacro { get; internal set; }

    /// <summary>The live reader the variable wall uses for WhaleGenie's own values.</summary>
    internal SystemVariableReader? SystemReader { get; set; }

    /// <summary>The public macro this definition was copied from, if any.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OriginLabel))]
    [NotifyPropertyChangedFor(nameof(HasOrigin))]
    public partial MacroOrigin? Origin { get; set; }

    public bool HasOrigin => Origin is not null;

    public string OriginLabel => Origin is { } origin
        ? Strings.Format("Editor.PublicOrigin", origin.Name, origin.Version)
        : string.Empty;

    /// <summary>
    /// Names of the other macros in the project. A step that calls a macro is checked against
    /// these, so a call left pointing at a name that has gone is called out here rather than
    /// waiting for the trigger to fire. The macro being edited is added by the check itself,
    /// because a macro may call itself.
    /// </summary>
    private IReadOnlyList<MacroCallable> _projectMacros = [];

    /// <summary>
    /// True when another macro of the same project also uses the browser. One browser serves the
    /// whole program, so a macro that changes tabs changes what the other one reads.
    /// </summary>
    private bool _browserShared;

    /// <summary>
    /// What this macro asks a call to fill in. The editor holds it so that opening a macro and
    /// saving it again does not drop what it declares, and so a call this macro makes to itself is
    /// judged by the same list.
    /// </summary>
    public List<MacroParameter> DeclaredParameters { get; } = [];

    /// <summary>The names this macro offers to hand back to a call.</summary>
    public List<MacroResult> DeclaredResults { get; } = [];

    /// <summary>
    /// The values this macro keeps for itself, with the value every run starts at. Held here for the
    /// same reason the interface is: opening a macro and saving it again must not drop what it
    /// declares, and the variable wall has to offer the names before a step happens to write one.
    /// </summary>
    public List<MacroLocalVariable> DeclaredLocals { get; } = [];

    /// <summary>This macro's own declarations, in the shape a call to it is read against.</summary>
    /// <remarks>
    /// The steps travel with the declarations because the call graph has to be able to follow a
    /// macro that calls itself. Without them a self-call looks like a call into a macro with no
    /// steps, so the input and browser analysis would miss everything behind the recursion.
    /// </remarks>
    private MacroCallable OwnCallable => new(Name, DeclaredParameters, DeclaredResults, Steps,
        DeclaredLocals);

    /// <summary>What the design-time check has to say about the steps as they stand now.</summary>
    public IReadOnlyList<MacroProblem> Problems { get; private set; } = [];

    /// <summary>What is wrong with this macro's own parameters and results, with no step to hang it on.</summary>
    public IReadOnlyList<string> DeclarationProblems { get; private set; } = [];

    public bool HasProblems => Problems.Count > 0;

    public bool HasStepProblems => Problems.Count > 0;

    public bool HasDeclarationProblems => DeclarationProblems.Count > 0;

    /// <summary>
    /// True while a declaration problem makes the macro impossible to call: an empty or unreadable
    /// name, a name that collides with the supplied-empty marker, a duplicate, a run-reserved name,
    /// or a required argument with a default. Warnings such as "this result is never written" do
    /// not block saving work that is still being written.
    /// </summary>
    public bool HasBlockingDeclarationProblems => OwnCallable.Problems().Count > 0;

    /// <summary>The save button's answer: the macro name and the declarations both have to be usable.</summary>
    public bool CanSave => !HasNameProblem && !HasBlockingDeclarationProblems
        && (IsSubMacro || !HasBlockingHotstringProblem);

    private bool HasBlockingHotstringProblem
        => (SelectedTrigger?.Kind == TriggerKind.Hotstring
            && IsBlockingHotstring(HotstringText, HotstringRequireEnd, HotstringEndChars))
            || TriggerEntries.Any(entry => !ReferenceEquals(entry, SelectedTrigger)
                && entry.Spec is HotstringSpec spec
                && IsBlockingHotstring(spec.Abbreviation, spec.RequireEndChar, spec.EndChars));

    private static bool IsBlockingHotstring(
        string abbreviation, bool requireEndChar, string endChars)
    {
        if (abbreviation.Length is 0 or > HotstringCharacters.MaximumLength
            || !HotstringCharacters.IsAllowed(abbreviation))
        {
            return true;
        }

        if (!requireEndChar)
        {
            return false;
        }

        var characters = HotstringCharacters.ParseEndChars(endChars);
        return characters.Count == 0
            || characters.Any(value =>
                value.Length != 1 || !HotstringCharacters.IsAllowed(value[0]));
    }

    /// <summary>
    /// One line saying how many calls in the project no longer match these declarations. The
    /// declarations are edited in this macro, but the calls they broke live in other macros, so
    /// the consequence is said here rather than left until each caller is opened.
    /// </summary>
    public string CallImpactSummary { get; private set; } = string.Empty;

    public bool HasCallImpact => CallImpactSummary.Length > 0;

    /// <summary>
    /// The one line the editor has to say about the steps: why the last drag, paste or structural
    /// edit was refused, or which step ids a file brought in that had to be repaired. It stays above
    /// the step list until another edit succeeds or the selection moves, so neither a refused drop
    /// nor a repaired file looks like nothing happened.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    public partial string Notice { get; set; } = string.Empty;

    public bool HasNotice => Notice.Length > 0;

    /// <summary>One line above the list saying how much the editor has to say about the steps.</summary>
    public string ProblemSummary => Strings.Format("Editor.ProblemSummary",
        Problems.Select(problem => problem.Step).Distinct().Count());

    /// <summary>The declaration problems, one per line, for the editor to show above the step list.</summary>
    public string DeclarationProblemSummary => string.Join("\n", DeclarationProblems);

    /// <summary>
    /// What is wrong with the name the macro is about to be saved under. A macro is called by its
    /// name, so an empty one cannot be called at all and a duplicate would make the project hold
    /// two macros that answer to the same call — one of them silently winning.
    /// </summary>
    public string? NameProblem => Name.Trim().Length == 0
        ? Strings.Get("Editor.Problem.EmptyMacroName")
        : _projectMacros.Any(macro =>
            string.Equals(macro.Name.Trim(), Name.Trim(), StringComparison.OrdinalIgnoreCase))
            ? Strings.Format("Editor.Problem.DuplicateMacroName", Name.Trim())
            : null;

    public bool HasNameProblem => NameProblem is not null;

    /// <summary>Which steps the check is unhappy about, and what it says about each one.</summary>
    private Dictionary<MacroStep, string> _problemByStep = [];

    /// <summary>Hands in the macro names a call may reach, and checks the steps again.</summary>
    public void SetProjectMacros(IEnumerable<MacroCallable> macros, bool browserShared = false)
    {
        _projectMacros = [.. macros];
        OnPropertyChanged(nameof(NameProblem));
        OnPropertyChanged(nameof(HasNameProblem));
        _browserShared = browserShared;
        RebuildRows();
    }

    public bool HasSelection => _selection.Count > 0;

    /// <summary>The steps highlighted in the action list.</summary>
    public IReadOnlyList<MacroStep> SelectedSteps => _selection;
}
