using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Execution;
using WhaleGenie.Models;

namespace WhaleGenie.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public MainViewModel()
    {
        Macros.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasMacros));
            OnPropertyChanged(nameof(HasUnsavedChanges));
            RefreshVisibleMacros();
        };
        Localization.Strings.Current.LanguageChanged += () => OnPropertyChanged(nameof(WindowTitle));
    }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    partial void OnSearchTextChanged(string value) => RefreshVisibleMacros();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SystemLabel))]
    [NotifyPropertyChangedFor(nameof(SystemHint))]
    public partial bool IsRunning { get; set; }

    /// <summary>
    /// Label of the system button, which names the state rather than the action: it reads
    /// "已启用" while the system is on, and pressing it turns the system off.
    /// </summary>
    public string SystemLabel => Localization.Strings.Get(IsRunning ? "Main.SystemEnabled" : "Main.SystemDisabled");

    /// <summary>
    /// Hint under the button. Which macros are running is already plain on their cards, so this
    /// only has to point at the key that switches the system.
    /// </summary>
    public string SystemHint => Localization.Strings.Get(
        IsRunning ? "Main.SystemDisableHint" : "Main.SystemEnableHint");

    public string Version { get; } = "v0.0.1";

    /// <summary>
    /// Whether anything in the list has been changed since the package was last written — macros
    /// added, edited, removed or armed or disarmed, and shared variables imported. The armed state
    /// counts because it is written into the package: a macro that was switched off here is off
    /// the next time the package is opened, so it is a change like any other.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    public partial bool IsDirty { get; set; }

    /// <summary>
    /// True when stopping the program would throw away something the user made. A project that was
    /// never saved and holds nothing is not one of those, so "close all" on a list that is only
    /// memory does not turn into a question at exit.
    /// </summary>
    public bool HasUnsavedChanges => IsDirty && (Macros.Count > 0 || CurrentPath is not null);

    /// <summary>Package file the macros are saved to, or <c>null</c> before the first save.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    [NotifyPropertyChangedFor(nameof(CurrentFolder))]
    public partial string? CurrentPath { get; set; }

    /// <summary>Title bar text, which names the open package once there is one.</summary>
    public string WindowTitle => CurrentPath is null
        ? BaseTitle
        : $"{BaseTitle} — {System.IO.Path.GetFileName(CurrentPath)}";

    /// <summary>
    /// The application name as the title bar shows it. The admin badge appears only when the
    /// process really is elevated: a badge that is always there says nothing, and it made the
    /// window claim rights the program did not have.
    /// </summary>
    private static string BaseTitle => ProcessRights.IsElevated
        ? Localization.Strings.Get("Main.Title") + Localization.Strings.Get("Main.AdminBadge")
        : Localization.Strings.Get("Main.Title");

    /// <summary>Folder holding the open package, used by "open location".</summary>
    public string? CurrentFolder => CurrentPath is null
        ? null
        : System.IO.Path.GetDirectoryName(CurrentPath);

    /// <summary>Macros shown in the list. Empty until the user adds one.</summary>
    public ObservableCollection<MacroItem> Macros { get; } = [];

    /// <summary>
    /// What the list actually shows: every macro, or the ones whose name matches what is in the
    /// search box. The box narrows the view rather than the project, so nothing about the macros
    /// themselves depends on what is typed there — closing the box puts them all back.
    /// </summary>
    public ObservableCollection<MacroItem> VisibleMacros { get; } = [];

    public bool HasMacros => Macros.Count > 0;

    /// <summary>True when there are macros but the search left none of them on screen.</summary>
    public bool HasNoMatch => Macros.Count > 0 && VisibleMacros.Count == 0;

    /// <summary>Puts the macros the search box lets through into the list the window shows.</summary>
    private void RefreshVisibleMacros()
    {
        var wanted = SearchText.Trim();
        var keeping = wanted.Length == 0
            ? Macros
            : Macros.Where(macro =>
                macro.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase));

        VisibleMacros.Clear();
        foreach (var macro in keeping.ToList())
        {
            VisibleMacros.Add(macro);
        }

        OnPropertyChanged(nameof(HasNoMatch));
    }

    /// <summary>Adds a macro created in the macro editor and assigns it a display name.</summary>
    public void AddMacro(MacroItem macro)
    {
        if (string.IsNullOrWhiteSpace(macro.Name))
        {
            macro.Name = $"Macro {Macros.Count + 1}";
        }

        Macros.Add(macro);
        MarkDirty();
    }

    /// <summary>Swaps an edited macro back into the list, keeping its on/off state.</summary>
    public void ReplaceMacro(MacroItem original, MacroItem edited)
    {
        var index = Macros.IndexOf(original);
        if (index < 0)
        {
            return;
        }

        edited.IsEnabled = original.IsEnabled;
        Macros[index] = edited;
        MarkDirty();
    }

    /// <summary>Removes a macro from the list.</summary>
    public void RemoveMacro(MacroItem macro)
    {
        Macros.Remove(macro);
        MarkDirty();
    }

    /// <summary>Removes every macro, used by "close all".</summary>
    public void ClearMacros()
    {
        Macros.Clear();
        MarkDirty();
    }

    /// <summary>Arms or disarms a macro; the macro list shows the state as a coloured dot.</summary>
    public void ToggleMacro(MacroItem macro)
    {
        macro.IsEnabled = !macro.IsEnabled;
        MarkDirty();
    }

    /// <summary>
    /// What the shared variables hold, one line each, so that a change made while they were open
    /// can be told from no change at all.
    /// </summary>
    /// <remarks>
    /// Nothing else watches them: the package carries the shared variables beside the macros, but
    /// they are not on the macro list, so nothing on that list moves when one is added, renamed or
    /// given another starting value.
    /// </remarks>
    public static IReadOnlyList<string> SharedVariables()
        => [.. VariableCatalog.Globals.Select(variable =>
            $"{variable.Name}\u001f{variable.Type}\u001f{variable.DefaultValue}\u001f{variable.Description}")];

    /// <summary>Whether the shared variables have moved on since <paramref name="before"/> was taken.</summary>
    public static bool SharedVariablesChangedSince(IReadOnlyList<string> before)
        => !before.SequenceEqual(SharedVariables());

    /// <summary>Notes that the shared variables themselves were changed.</summary>
    public void MarkSharedVariablesChanged() => MarkDirty();

    /// <summary>Writes the macro list and the shared variables to a package file.</summary>
    public void SavePackage(string path)
    {
        Storage.MacroPackage.Save(path, [.. Macros], [.. VariableCatalog.Globals], Version);
        CurrentPath = path;
        IsDirty = false;
    }

    /// <summary>Replaces everything with the contents of a package file.</summary>
    public void OpenPackage(string path)
    {
        var contents = Storage.MacroPackage.Load(path);

        Macros.Clear();
        foreach (var macro in contents.Macros)
        {
            Macros.Add(macro);
        }

        VariableCatalog.Globals.Clear();
        foreach (var global in contents.Globals)
        {
            VariableCatalog.Globals.Add(global);
        }

        CurrentPath = path;
        IsDirty = false;
    }

    /// <summary>Adds the macros of a package file to the list without replacing what is there.</summary>
    public void ImportPackage(string path)
    {
        var contents = Storage.MacroPackage.Load(path);

        foreach (var macro in contents.Macros)
        {
            AddMacro(macro);
        }

        foreach (var global in contents.Globals)
        {
            if (!VariableCatalog.IsGlobal(global.Name))
            {
                VariableCatalog.Globals.Add(global);
            }
        }
    }

    /// <summary>
    /// Puts the work a stopped run left behind back into the list. It counts as unsaved on
    /// purpose: a snapshot is what a program that stopped without notice was holding, so the list
    /// has to keep asking to write it out rather than treating it as something already on disk.
    /// </summary>
    public void RestoreFrom(Storage.RecoveryContents contents)
    {
        Macros.Clear();
        foreach (var macro in contents.Macros)
        {
            Macros.Add(macro);
        }

        VariableCatalog.Globals.Clear();
        foreach (var global in contents.Globals)
        {
            VariableCatalog.Globals.Add(global);
        }

        CurrentPath = contents.PackagePath;
        MarkDirty();
    }

    [RelayCommand]
    private void ToggleRun() => IsRunning = !IsRunning;

    /// <summary>
    /// Raised for every change to the work this holds, where <see cref="IsDirty"/> only says
    /// whether there is any of it: the snapshot keeping up with the work has to hear about the
    /// changes that arrive while it is already unsaved, and the dirty flag does not move for
    /// those.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>Notes that the work has moved on.</summary>
    private void MarkDirty()
    {
        IsDirty = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
