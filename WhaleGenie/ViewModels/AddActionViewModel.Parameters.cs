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

// Building the fields of an action, including the arguments and results of a macro it calls.
public partial class AddActionViewModel
{
    private void BuildParameters(ActionDefinition? definition)
    {
        foreach (var parameter in Parameters)
        {
            parameter.PropertyChanged -= OnParameterChanged;

            if (parameter.List is not null)
            {
                parameter.List.PropertyChanged -= OnParameterChanged;
            }
        }

        Parameters.Clear();
        ReturnsRows.Clear();

        if (definition is not null)
        {
            var variables = CollectVariables();
            var steps = StepsOfferedTo(definition);

            // A call's form is built for whichever macro it names, so the name is put back into the
            // box it was read from before the fields that only exist because of it.
            var calling = CallsMacro(definition);
            _buildingCallFields = calling;
            try
            {
                foreach (var parameter in definition.Parameters)
                {
                    // A call's arguments are the called macro's declarations rather than settings
                    // of this action, so they are built as one list read off that macro. One end of
                    // the form asks what goes in and the other asks what comes back out, and both
                    // come from the macro being called.
                    if (parameter.Kind is ActionParameterKind.Arguments)
                    {
                        FillCallArguments();
                        continue;
                    }

                    var editor = AddEditor(parameter, CalledResults(definition), variables, steps);
                    if (calling && parameter.Name == "macro" && _callMacro.Length > 0)
                    {
                        editor.WriteValue(Formula.Text(_callMacro));
                    }
                }
            }
            finally
            {
                _buildingCallFields = false;
            }

            _callMacro = Parameter("macro")?.CurrentText.Trim() ?? string.Empty;
            MarkCoordinates();
            MarkRegion();
            MarkOffset();
            WireWindowPreview();
            RefreshClipboardPreview();

            if (ReturnsField is { } returns)
            {
                foreach (var row in returns.ReturnsRows)
                {
                    ReturnsRows.Add(row);
                }

                OnPropertyChanged(nameof(HasReturns));
            }

            SuggestReturnNames();

            foreach (var editor in Parameters.Where(field => field.IsWindowRule))
            {
                editor.WindowAboveRule = WindowAbove;
            }

            // 新的这一步如果还没指定窗口，而落点之上已经指定过，就默认「沿用上一次」。指向而不是
            // 复制：上面那一步改了窗口，这一串跟着改。
            if (_isNewStep
                && _followsWindowAbove
                && Parameter("window") is { } window
                && window.CurrentText.Trim().Length == 0)
            {
                window.FollowWindowRule();
            }
        }

        BuildRows();
        OpenFirstPage();
        OnParameterChanged(this, new PropertyChangedEventArgs(nameof(Parameters)));
    }

    /// <summary>Puts one field of the form up, and has the dialog follow it.</summary>
    private StepParameterViewModel AddEditor(ActionParameter parameter,
        IReadOnlyList<ActionReturn> results,
        IReadOnlyList<VariableChoice> variables, IReadOnlyList<ActionParameterOption> steps)
    {
        var editor = new StepParameterViewModel(parameter, variables, _macros, steps, results);
        editor.AssetFolder = _assetFolder;
        editor.PropertyChanged += OnParameterChanged;

        if (editor.List is not null)
        {
            editor.List.PropertyChanged += OnParameterChanged;
        }

        Parameters.Add(editor);
        return editor;
    }

    /// <summary>The macro the call names right now, or null while it names one that is not there.</summary>
    private MacroCallable? CalledMacro()
    {
        var wanted = Parameter("macro")?.CurrentText.Trim() ?? string.Empty;
        return wanted.Length == 0
            ? null
            : _macros.FirstOrDefault(call =>
                string.Equals(call.Name.Trim(), wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Builds the rows a call fills in: one per argument the called macro declares, carrying that
    /// macro's own words about what each one is. A macro that declares nothing asks for nothing, so
    /// the form says only which macro and what to bring back.
    /// </summary>
    private void FillCallArguments()
    {
        CallArgumentRows.Clear();

        // What the file said about these arguments counts only while the call still names the macro
        // that file named. Switching to another macro shows what that one asks for; coming back puts
        // the file's own values back with it.
        var loaded = string.Equals(_loadedFor, _callMacro, StringComparison.OrdinalIgnoreCase)
            ? _loadedArguments.ToDictionary(row => row.Name, StringComparer.OrdinalIgnoreCase)
            : [];

        if (CalledMacro() is { } call)
        {
            foreach (var argument in CallableSignature.Arguments(call))
            {
                loaded.TryGetValue(argument.Name, out var written);
                CallArgumentRows.Add(new CallArgumentRowViewModel(
                    argument.Name, argument.Hint, argument.Required, declared: true,
                    written.Given, written.Value ?? string.Empty));
            }
        }

        // A row the file holds for an argument this macro no longer declares keeps its place. It is
        // what a rename or a removed argument leaves behind, and dropping it here would delete a
        // value the author typed without saying anything.
        foreach (var (name, value, given) in loaded.Values)
        {
            if (CallArgumentRows.Any(row =>
                    string.Equals(row.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            CallArgumentRows.Add(new CallArgumentRowViewModel(
                name, string.Empty, required: false, declared: false, given, value));
        }

        OnPropertyChanged(nameof(HasCallArguments));
    }

    /// <summary>
    /// Writes the words under each argument row again in the interface language. They are read off
    /// the called macro's declaration, so they are rebuilt here rather than stored in the row.
    /// </summary>
    private void RefreshCallArgumentHints()
    {
        var call = CalledMacro();
        foreach (var row in CallArgumentRows)
        {
            row.SetHint(call is null
                ? string.Empty
                : CallableSignature.Argument(call, row.Name)?.Hint ?? string.Empty);
        }
    }

    /// <summary>
    /// Reads the arguments an existing step passes, before the rows are built: a row naming an
    /// argument the called macro no longer declares has nothing in the declaration to come from, so
    /// it has to be remembered here or it would be gone the moment the step is opened.
    /// </summary>
    private void LoadCallArguments(MacroStep step)
    {
        _loadedArguments =
        [
            .. step.Parameters
                .Where(parameter => parameter.Name == FieldNames.Arguments)
                .SelectMany(parameter => parameter.Rows)
                .Select(row => (
                    Name: row.Text(FieldNames.Name).Trim(),
                    Value: row.Text(FieldNames.Value),
                    Given: string.Equals(row.Text(FieldNames.Given).Trim(), "true",
                        StringComparison.OrdinalIgnoreCase)))
                .Where(row => row.Name.Length > 0),
        ];

        _loadedFor = _callMacro;
        FillCallArguments();
    }

    /// <summary>
    /// What the results page of this step lists: an ordinary action's own declared results, or —
    /// for a call — the names the called macro offers to hand back, so the ticks are exactly the
    /// ones that call can make.
    /// </summary>
    private IReadOnlyList<ActionReturn> CalledResults(ActionDefinition definition)
    {
        if (!CallsMacro(definition) || CalledMacro() is not { } call)
        {
            return definition.Returns;
        }

        return
        [
            .. CallableSignature.Results(call).Select(result => new ActionReturn
            {
                Key = result.Name,
                Label = result.Name,
                Hint = result.Hint,
                Type = result.Type,
            }),
        ];
    }

    /// <summary>
    /// Rebuilds a call's own fields when the macro it names changes. The values are kept per macro
    /// on the way out and put back on the way in, so what was typed for one macro is not lost by
    /// looking at another.
    /// </summary>
    private void RefreshCallFields()
    {
        var wanted = Parameter("macro")?.CurrentText.Trim() ?? string.Empty;
        RememberCall();

        // The fields are built for the macro being named, so the name is settled before the build:
        // what the build reads off "which macro" decides the fields that come after it.
        _callMacro = wanted;
        BuildParameters(SelectedDefinition);
        RestoreCall(wanted);
        OnPropertyChanged(nameof(CanSave));
        SaveCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Keeps what the call's own fields hold, under the macro they were filled in for.</summary>
    private void RememberCall()
    {
        if (_callMacro.Length == 0)
        {
            return;
        }

        var arguments = new Dictionary<string, (string Value, bool Given)>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var row in CallArgumentRows)
        {
            arguments[row.Name] = (row.Value, row.Given);
        }

        var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in ReturnsRows.Where(row => row.Ticked && row.Name.Trim().Length > 0))
        {
            results[row.Key] = row.Name.Trim();
        }

        _callFill[CallFillKey(_callMacro)] = (arguments, results);
    }

    /// <summary>Puts back what was filled in for one macro, if it has been filled in before.</summary>
    private void RestoreCall(string macro)
    {
        if (!_callFill.TryGetValue(CallFillKey(macro), out var filled))
        {
            return;
        }

        foreach (var row in CallArgumentRows)
        {
            if (filled.Arguments.TryGetValue(row.Name, out var kept))
            {
                row.Load(kept.Value, kept.Given);
            }
        }

        if (ReturnsField is not { } returns)
        {
            return;
        }

        foreach (var row in returns.ReturnsRows)
        {
            if (filled.Results.TryGetValue(row.Key, out var name))
            {
                row.Load(name);
            }
        }

        OnPropertyChanged(nameof(HasReturns));
    }

    /// <summary>Which step and which macro a set of filled-in call values belongs to.</summary>
    private string CallFillKey(string macro) => _editingId + "\u001f" + macro;
}
