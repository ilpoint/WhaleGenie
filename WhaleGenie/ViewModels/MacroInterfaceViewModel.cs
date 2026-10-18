using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.ViewModels;

/// <summary>One choice in the kind dropdowns of the macro interface.</summary>
public sealed record MacroTypeChoice(ActionReturnType Value, string Label);

/// <summary>
/// The macro's own parameters and results: what a call has to fill in, and what the macro may hand
/// back. The rows write straight into the declaration lists the editor saves and the call site is
/// read against, so the interface cannot drift into a second copy.
/// </summary>
public partial class MacroInterfaceViewModel : ViewModelBase
{
    private readonly MacroEditorViewModel _editor;

    public MacroInterfaceViewModel(MacroEditorViewModel editor)
    {
        _editor = editor;
        Types = BuildTypes();

        Reload();
        Changed += () => _editor.RefreshDeclarations();
        _editor.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MacroEditorViewModel.DeclarationProblemSummary))
            {
                OnPropertyChanged(nameof(ProblemSummary));
            }
        };
    }

    public IReadOnlyList<MacroTypeChoice> Types { get; private set; }

    /// <summary>
    /// The values a default can read. A default is evaluated at the call site, not in the called
    /// macro's own table, so this lists the global and system variables a caller always has. A
    /// caller's local name can still be typed by hand; it cannot be enumerated here.
    /// </summary>
    public IReadOnlyList<VariableChoice> ValueVariables => VariableChoices.For([]);

    private static IReadOnlyList<MacroTypeChoice> BuildTypes() =>
    [
        new(ActionReturnType.Text, Strings.Get("MacroInterface.Type.Text")),
        new(ActionReturnType.Number, Strings.Get("MacroInterface.Type.Number")),
        new(ActionReturnType.Flag, Strings.Get("MacroInterface.Type.Flag")),
        new(ActionReturnType.List, Strings.Get("MacroInterface.Type.List")),
        new(ActionReturnType.Record, Strings.Get("MacroInterface.Type.Record")),
        new(ActionReturnType.Image, Strings.Get("MacroInterface.Type.Image")),
    ];

    /// <summary>
    /// A language switch reaches the kind names the rows hold copies of, so they are rebuilt along
    /// with the rows rather than left in the language the window was opened in.
    /// </summary>
    public override void RefreshLanguage()
    {
        Types = BuildTypes();
        OnPropertyChanged(nameof(Types));
        Reload();
    }

    public ObservableCollection<MacroParameterRow> Parameters { get; } = [];

    public ObservableCollection<MacroResultRow> Results { get; } = [];

    /// <summary>Raised when a row changed, so the editor can check the declarations again.</summary>
    public event Action? Changed;

    public string ProblemSummary => _editor.DeclarationProblemSummary;

    public bool HasParameters => Parameters.Count > 0;

    public bool HasResults => Results.Count > 0;

    /// <summary>Reads the editor's declarations back into rows, after a lift or a fresh load.</summary>
    public void Reload()
    {
        Parameters.Clear();
        foreach (var parameter in _editor.DeclaredParameters)
        {
            Parameters.Add(new MacroParameterRow(parameter, Types, OnRowChanged));
        }

        Results.Clear();
        foreach (var result in _editor.DeclaredResults)
        {
            Results.Add(new MacroResultRow(result, Types, OnRowChanged));
        }

        OnPropertyChanged(nameof(HasParameters));
        OnPropertyChanged(nameof(HasResults));
    }

    [RelayCommand]
    private void AddParameter()
    {
        var parameter = new MacroParameter { Name = NextName(Strings.Get("MacroInterface.DefaultParameterName")), Required = false };
        _editor.DeclaredParameters.Add(parameter);
        Parameters.Add(new MacroParameterRow(parameter, Types, OnRowChanged));
        OnPropertyChanged(nameof(HasParameters));
        Changed?.Invoke();
    }

    [RelayCommand]
    private void RemoveParameter(MacroParameterRow? row)
    {
        if (row is null)
        {
            return;
        }

        _editor.DeclaredParameters.Remove(row.Model);
        Parameters.Remove(row);
        OnPropertyChanged(nameof(HasParameters));
        Changed?.Invoke();
    }

    [RelayCommand]
    private void AddResult()
    {
        var result = new MacroResult { Name = NextName(Strings.Get("MacroInterface.DefaultResultName")) };
        _editor.DeclaredResults.Add(result);
        Results.Add(new MacroResultRow(result, Types, OnRowChanged));
        OnPropertyChanged(nameof(HasResults));
        Changed?.Invoke();
    }

    [RelayCommand]
    private void RemoveResult(MacroResultRow? row)
    {
        if (row is null)
        {
            return;
        }

        _editor.DeclaredResults.Remove(row.Model);
        Results.Remove(row);
        OnPropertyChanged(nameof(HasResults));
        Changed?.Invoke();
    }

    private string NextName(string stem)
    {
        var index = 1;
        while (_editor.DeclaredParameters.Any(parameter =>
                   string.Equals(parameter.Name, stem + index, StringComparison.OrdinalIgnoreCase))
               || _editor.DeclaredResults.Any(result =>
                   string.Equals(result.Name, stem + index, StringComparison.OrdinalIgnoreCase)))
        {
            index++;
        }

        return stem + index;
    }

    private void OnRowChanged()
    {
        OnPropertyChanged(nameof(HasParameters));
        OnPropertyChanged(nameof(HasResults));
        Changed?.Invoke();
    }
}

/// <summary>One declared parameter, edited in place.</summary>
public sealed class MacroParameterRow : ViewModelBase
{
    private readonly Action _changed;

    internal MacroParameterRow(MacroParameter model, IReadOnlyList<MacroTypeChoice> types,
        Action changed)
    {
        Model = model;
        Types = types;
        _changed = changed;
    }

    internal MacroParameter Model { get; }

    public IReadOnlyList<MacroTypeChoice> Types { get; }

    public string Name
    {
        get => Model.Name;
        set
        {
            if (Model.Name == value)
            {
                return;
            }

            Model.Name = value;
            OnPropertyChanged();
            _changed();
        }
    }

    public MacroTypeChoice SelectedType
    {
        get => Types.First(choice => choice.Value == Model.Type);
        set
        {
            if (Model.Type == value.Value)
            {
                return;
            }

            Model.Type = value.Value;
            OnPropertyChanged();
            _changed();
        }
    }

    public string Default
    {
        get => Model.Default;
        set
        {
            if (Model.Default == value)
            {
                return;
            }

            Model.Default = value;
            OnPropertyChanged();
            _changed();
        }
    }

    public bool Required
    {
        get => Model.Required;
        set
        {
            if (Model.Required == value)
            {
                return;
            }

            Model.Required = value;
            OnPropertyChanged();
            _changed();
        }
    }

    public string Description
    {
        get => Model.Description;
        set
        {
            if (Model.Description == value)
            {
                return;
            }

            Model.Description = value;
            OnPropertyChanged();
            _changed();
        }
    }
}

/// <summary>One declared result, edited in place.</summary>
public sealed class MacroResultRow : ViewModelBase
{
    private readonly Action _changed;

    internal MacroResultRow(MacroResult model, IReadOnlyList<MacroTypeChoice> types, Action changed)
    {
        Model = model;
        Types = types;
        _changed = changed;
    }

    internal MacroResult Model { get; }

    public IReadOnlyList<MacroTypeChoice> Types { get; }

    public string Name
    {
        get => Model.Name;
        set
        {
            if (Model.Name == value)
            {
                return;
            }

            Model.Name = value;
            OnPropertyChanged();
            _changed();
        }
    }

    public MacroTypeChoice SelectedType
    {
        get => Types.First(choice => choice.Value == Model.Type);
        set
        {
            if (Model.Type == value.Value)
            {
                return;
            }

            Model.Type = value.Value;
            OnPropertyChanged();
            _changed();
        }
    }

    public string Description
    {
        get => Model.Description;
        set
        {
            if (Model.Description == value)
            {
                return;
            }

            Model.Description = value;
            OnPropertyChanged();
            _changed();
        }
    }
}
