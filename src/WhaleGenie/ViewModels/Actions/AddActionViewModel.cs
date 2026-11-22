using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
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

/// <summary>
/// Drives the "Add Action" dialog: fill in the parameters the action asks for, then save the
/// result as one step. Which action that is comes from the editor, which is the only thing that
/// knows where the step is going.
/// </summary>
public partial class AddActionViewModel : ViewModelBase
{
    /// <summary>Where a global created here keeps its value between runs.</summary>
    public PersistedVariables Persisted { get; init; } = new();

    private IReadOnlyList<VariableChoice> _variables;
    private IReadOnlyList<MacroCallable> _macros;
    private IReadOnlyList<ActionDefinition> _availableActions = [];
    private IReadOnlyList<ActionParameterOption> _steps = [];
    private string _assetFolder = string.Empty;

    /// <summary>
    /// The loop variables the place this step is going already uses. A counting loop starts its
    /// counter at a name none of them is using, so a loop nested in another does not take the outer
    /// one's counter — which would lose the outer value when the inner loop ends.
    /// </summary>
    private readonly IReadOnlyCollection<string> _loopNames;

    /// <summary>
    /// True when the form's arguments and results are declarations of the macro this step names.
    /// The catalogue says that, so another action with the same shape needs no branch here.
    /// </summary>
    private static bool CallsMacro(ActionDefinition? definition)
        => definition?.SignatureSource is ActionSignatureSource.CalledMacro;

    /// <summary>
    /// The action that lists windows rather than acting on one of them. Nothing written in its match
    /// text means every window there is, so the list under that field has to say so rather than say
    /// "the window in front", which is what the same empty text means to every other window action.
    /// </summary>
    private const string ListWindowsKey = "window.list";

    /// <summary>
    /// 落点之上已经有步骤指定过窗口，所以新建的这一步默认「沿用上一次」。在同一个窗口里做十件事，
    /// 前面九次不必各自再绑一遍。
    /// </summary>
    private readonly bool _followsWindowAbove;

    /// <summary>The macro the call's own fields were built for, so a change to it can be noticed.</summary>
    private string _callMacro = string.Empty;

    /// <summary>
    /// 本宏里已经用过的那几条窗口规则，按第一次出现的顺序，带用了几处。窗口那一格的下拉里列出它们，
    /// 选一条就等于把那段规则原样写进来，不必重新捕获。
    /// </summary>
    public IReadOnlyList<WindowRuleChoice> WindowRules { get; }

    /// <summary>
    /// 落点之上最近那一条真的窗口规则：窗口格写「沿用上一次」时，它就是要跟到的那一个。摘要里把它
    /// 写出来，试运行也按它走——试的才是运行时会发生的事。
    /// </summary>
    public string WindowAbove { get; }

    /// <summary>True while a call's fields are being rebuilt, so putting the name back is not taken
    /// for the user picking another macro.</summary>
    private bool _buildingCallFields;

    /// <summary>
    /// What was filled into a call's fields, kept by the macro those fields were filled for:
    /// switching to another macro and back leaves the values where the author left them rather
    /// than emptying the form on the way through.
    /// </summary>
    private readonly Dictionary<string, (Dictionary<string, (string Value, bool Given)> Arguments,
        Dictionary<string, string> Results)> _callFill = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The argument rows the step was opened with, and the macro they were written for. They are
    /// kept apart from what the form holds now so a renamed argument can be told from another
    /// macro: the rows written for a macro this call no longer names are not this call's to show.
    /// </summary>
    private IReadOnlyList<(string Name, string Value, bool Given)> _loadedArguments = [];

    /// <summary>The macro those loaded rows belong to, or empty when the step was just created.</summary>
    private string _loadedFor = string.Empty;

    /// <summary>Raised with the step to add, or <c>null</c> when the dialog is cancelled.</summary>
    public event Action<MacroStep?>? CloseRequested;

    /// <summary>Creates the dialog on the actions the place a step is going accepts.</summary>
    /// <param name="actions">
    /// The actions that place takes — a switch's branch list only takes branches. Only the editor
    /// knows which list a step is going into, so this is given rather than defaulted: a list
    /// nobody chose is how the dialog came to offer the whole catalogue beside a form that had
    /// already been decided.
    /// </param>
    /// <summary>
    /// The project's shared values, so a trial run here reads what the editor reads. A form built
    /// on its own gets a session of its own.
    /// </summary>
    public VariableSession Variables { get; init; } = new();

    public AddActionViewModel(IReadOnlyList<ActionDefinition> actions,
        IReadOnlyList<VariableChoice>? variables = null, IReadOnlyList<MacroCallable>? macros = null,
        IReadOnlyList<ActionParameterOption>? steps = null, string? newStepId = null,
        IReadOnlyList<WindowRuleChoice>? windowRules = null,
        bool followsWindowAbove = false, string? windowAbove = null,
        IReadOnlyCollection<string>? loopNames = null)
    {
        AvailableActions = actions;
        _variables = variables ?? [];
        _macros = macros ?? [];
        StepChoices = steps ?? [];
        WindowRules = windowRules ?? [];
        _loopNames = loopNames ?? [];
        _followsWindowAbove = followsWindowAbove;
        WindowAbove = windowAbove ?? string.Empty;

        // A step being written has a name already, so the fields that create variables can carry it.
        // The editor hands one in for a new step; an existing step brings its own when it is loaded.
        _editingId = newStepId ?? string.Empty;
        _isNewStep = _editingId.Length > 0;
        // A step that fails is usually a surprise the macro cannot have planned for, so a new one
        // starts by stopping and asking rather than bringing the whole run down on its own. That
        // is the same set of settings every step the editor makes starts with, the one place
        // StepMeta.New keeps them.
        MetaOnError = ErrorChoices.First(choice => choice.Value == StepMeta.Name(StepMeta.New.OnError));
        MetaRetryBackoff = BackoffChoices[0];
        MetaTimeout = new DurationSettingViewModel();
        MetaRetryDelay = new DurationSettingViewModel(500);
        MetaDelayBefore = new DurationSettingViewModel();
        MetaDelayAfter = new DurationSettingViewModel();

        // A length of time is held as a number plus the unit it reads in, so a change to either has
        // to refresh the preview the same way a change to an action parameter does.
        foreach (var setting in new[] { MetaTimeout, MetaRetryDelay, MetaDelayBefore, MetaDelayAfter })
        {
            setting.PropertyChanged += OnSettingChanged;
        }

        foreach (var (key, title) in new[]
                 {
                     (BasePage, "Add.Page.Base"),
                     (ExecPage, "Add.Page.Exec"),
                     (AdvancedPage, "Add.Page.Advanced"),
                     (OtherPage, "Add.Page.Other"),
                 })
        {
            Pages.Add(new ActionPageViewModel { Key = key, TitleKey = title });
        }

        Page = Pages[0];
    }
}
