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
        RefreshOrder();
    }

    /// <summary>
    /// Tells each row whether anything is above or below it. The order is the declaration's order,
    /// which is the order a call form and the debug panel's parameter list follow, so a row that
    /// cannot move says so instead of offering a button that does nothing.
    /// </summary>
    private void RefreshOrder()
    {
        for (var index = 0; index < Parameters.Count; index++)
        {
            Parameters[index].SetPlace(index, Parameters.Count);
        }

        for (var index = 0; index < Results.Count; index++)
        {
            Results[index].SetPlace(index, Results.Count);
        }
    }

    [RelayCommand]
    private void MoveParameterUp(MacroParameterRow? row) => MoveParameter(row, -1);

    [RelayCommand]
    private void MoveParameterDown(MacroParameterRow? row) => MoveParameter(row, 1);

    /// <summary>
    /// Moves one declaration one place up or down. Both the rows and the editor's own list move, so
    /// what the window shows and what gets saved are the same order at every moment.
    /// </summary>
    private void MoveParameter(MacroParameterRow? row, int offset)
    {
        if (row is null)
        {
            return;
        }

        var from = Parameters.IndexOf(row);
        var to = from + offset;
        if (from < 0 || to < 0 || to >= Parameters.Count)
        {
            return;
        }

        Parameters.Move(from, to);
        MoveIn(_editor.DeclaredParameters, row.Model, to);
        RefreshOrder();
        Changed?.Invoke();
    }

    [RelayCommand]
    private void MoveResultUp(MacroResultRow? row) => MoveResult(row, -1);

    [RelayCommand]
    private void MoveResultDown(MacroResultRow? row) => MoveResult(row, 1);

    private void MoveResult(MacroResultRow? row, int offset)
    {
        if (row is null)
        {
            return;
        }

        var from = Results.IndexOf(row);
        var to = from + offset;
        if (from < 0 || to < 0 || to >= Results.Count)
        {
            return;
        }

        Results.Move(from, to);
        MoveIn(_editor.DeclaredResults, row.Model, to);
        RefreshOrder();
        Changed?.Invoke();
    }

    private static void MoveIn<T>(List<T> list, T item, int to)
    {
        var at = list.IndexOf(item);
        if (at < 0 || at == to)
        {
            return;
        }

        list.RemoveAt(at);
        list.Insert(to, item);
    }

    [RelayCommand]
    private void AddParameter()
    {
        // Required by default, which is what the model and the file already say. An optional
        // parameter is a value that quietly arrives empty when a call leaves it out, and that is the
        // kind of silence the declaration rules exist to prevent — so leaving it out is the edit
        // somebody has to mean, not the one they get for not thinking about it.
        var parameter = new MacroParameter
        {
            Name = NextName(Strings.Get("MacroInterface.DefaultParameterName")),
            Required = true,
        };
        _editor.DeclaredParameters.Add(parameter);
        Parameters.Add(new MacroParameterRow(parameter, Types, OnRowChanged));
        OnPropertyChanged(nameof(HasParameters));
        RefreshOrder();
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
        RefreshOrder();
        Changed?.Invoke();
    }

    [RelayCommand]
    private void AddResult()
    {
        var result = new MacroResult { Name = NextName(Strings.Get("MacroInterface.DefaultResultName")) };
        _editor.DeclaredResults.Add(result);
        Results.Add(new MacroResultRow(result, Types, OnRowChanged));
        OnPropertyChanged(nameof(HasResults));
        RefreshOrder();
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
        RefreshOrder();
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
public sealed partial class MacroParameterRow : ViewModelBase
{
    private readonly Action _changed;

    internal MacroParameterRow(MacroParameter model, IReadOnlyList<MacroTypeChoice> types,
        Action changed)
    {
        Model = model;
        Types = types;
        _changed = changed;
        LoadFields();
    }

    /// <summary>
    /// What this record holds, one row per field. Only a record has fields, so the rows stay out of
    /// the way until the kind says so — and they are kept, not cleared, when the kind changes:
    /// switching to text and back is a correction, not a decision to throw the work away.
    /// </summary>
    public ObservableCollection<MacroFieldRow> Fields { get; } = [];

    public bool HasFields => Fields.Count > 0;

    /// <summary>True when the declaration says its value is a record, which is the only kind with fields.</summary>
    public bool IsRecord => Model.Type == ActionReturnType.Record;

    /// <summary>>Reads the model's field list back into rows, after an edit or a language switch.</summary>
    internal void LoadFields()
    {
        Fields.Clear();
        foreach (var field in Model.Fields)
        {
            Fields.Add(new MacroFieldRow(field, Types, OnFieldChanged));
        }

        OnPropertyChanged(nameof(HasFields));
    }

    [RelayCommand]
    private void AddField()
    {
        var field = new MacroField { Name = NextFieldName() };
        Model.Fields.Add(field);
        Fields.Add(new MacroFieldRow(field, Types, OnFieldChanged));
        OnPropertyChanged(nameof(HasFields));
        _changed();
    }

    [RelayCommand]
    private void RemoveField(MacroFieldRow? row)
    {
        if (row is null)
        {
            return;
        }

        Model.Fields.Remove(row.Model);
        Fields.Remove(row);
        OnPropertyChanged(nameof(HasFields));
        _changed();
    }

    private string NextFieldName()
    {
        var stem = Strings.Get("MacroInterface.DefaultFieldName");
        var index = 1;
        while (Model.Fields.Any(field =>
                   string.Equals(field.Name, stem + index, StringComparison.OrdinalIgnoreCase)))
        {
            index++;
        }

        return stem + index;
    }

    /// <summary>
    /// A field edit is a declaration edit: the variable wall and the check that reads `$rec.field`
    /// both answer differently the moment a field name changes.
    /// </summary>
    private void OnFieldChanged()
    {
        OnPropertyChanged(nameof(Fields));
        _changed();
    }

    internal MacroParameter Model { get; }

    public IReadOnlyList<MacroTypeChoice> Types { get; }

    /// <summary>True when something is declared above this row, so it can move up.</summary>
    public bool CanMoveUp { get; private set; }

    /// <summary>True when something is declared below this row, so it can move down.</summary>
    public bool CanMoveDown { get; private set; }

    /// <summary>Says where the row stands, for the two buttons that would move it.</summary>
    internal void SetPlace(int index, int count)
    {
        CanMoveUp = index > 0;
        CanMoveDown = index >= 0 && index < count - 1;
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
    }

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

            var wasRecord = IsRecord;
            Model.Type = value.Value;
            OnPropertyChanged();

            // The field rows only show for a record, so the answer to "is this a record" has to be
            // repeated whenever the kind changes — including when a record becomes something else.
            if (wasRecord != IsRecord)
            {
                OnPropertyChanged(nameof(IsRecord));
            }

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
public sealed partial class MacroResultRow : ViewModelBase
{
    private readonly Action _changed;

    internal MacroResultRow(MacroResult model, IReadOnlyList<MacroTypeChoice> types, Action changed)
    {
        Model = model;
        Types = types;
        _changed = changed;
        LoadFields();
    }

    /// <summary>What a record result hands over, one row per field.</summary>
    public ObservableCollection<MacroFieldRow> Fields { get; } = [];

    public bool HasFields => Fields.Count > 0;

    public bool IsRecord => Model.Type == ActionReturnType.Record;

    internal void LoadFields()
    {
        Fields.Clear();
        foreach (var field in Model.Fields)
        {
            Fields.Add(new MacroFieldRow(field, Types, OnFieldChanged));
        }

        OnPropertyChanged(nameof(HasFields));
    }

    [RelayCommand]
    private void AddField()
    {
        var field = new MacroField { Name = NextFieldName() };
        Model.Fields.Add(field);
        Fields.Add(new MacroFieldRow(field, Types, OnFieldChanged));
        OnPropertyChanged(nameof(HasFields));
        _changed();
    }

    [RelayCommand]
    private void RemoveField(MacroFieldRow? row)
    {
        if (row is null)
        {
            return;
        }

        Model.Fields.Remove(row.Model);
        Fields.Remove(row);
        OnPropertyChanged(nameof(HasFields));
        _changed();
    }

    private string NextFieldName()
    {
        var stem = Strings.Get("MacroInterface.DefaultFieldName");
        var index = 1;
        while (Model.Fields.Any(field =>
                   string.Equals(field.Name, stem + index, StringComparison.OrdinalIgnoreCase)))
        {
            index++;
        }

        return stem + index;
    }

    private void OnFieldChanged()
    {
        OnPropertyChanged(nameof(Fields));
        _changed();
    }

    internal MacroResult Model { get; }

    public IReadOnlyList<MacroTypeChoice> Types { get; }

    public bool CanMoveUp { get; private set; }

    public bool CanMoveDown { get; private set; }

    internal void SetPlace(int index, int count)
    {
        CanMoveUp = index > 0;
        CanMoveDown = index >= 0 && index < count - 1;
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
    }

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

            var wasRecord = IsRecord;
            Model.Type = value.Value;
            OnPropertyChanged();

            if (wasRecord != IsRecord)
            {
                OnPropertyChanged(nameof(IsRecord));
            }

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

/// <summary>
/// One field of a record declaration: the name `$record.name` reads and what it holds. A record
/// without fields is a bag of names nobody can check before the run, so this row is where they are
/// written down once instead of being discovered by the run.
/// </summary>
public sealed class MacroFieldRow : ViewModelBase
{
    private readonly Action _changed;

    internal MacroFieldRow(MacroField model, IReadOnlyList<MacroTypeChoice> types, Action changed)
    {
        Model = model;
        Types = types;
        _changed = changed;
    }

    internal MacroField Model { get; }

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
}
