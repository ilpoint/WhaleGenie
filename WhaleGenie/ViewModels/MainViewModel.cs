using System;
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
    }

    /// <summary>Removes a macro from the list.</summary>
    public void RemoveMacro(MacroItem macro) => Macros.Remove(macro);

    /// <summary>Removes every macro, used by "close all".</summary>
    public void ClearMacros() => Macros.Clear();

    /// <summary>Arms or disarms a macro; the macro list shows the state as a coloured dot.</summary>
    public void ToggleMacro(MacroItem macro) => macro.IsEnabled = !macro.IsEnabled;

    /// <summary>Writes the macro list and the shared variables to a package file.</summary>
    public void SavePackage(string path)
    {
        Storage.MacroPackage.Save(path, [.. Macros], [.. VariableCatalog.Globals], Version);
        CurrentPath = path;
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

    [RelayCommand]
    private void ToggleRun() => IsRunning = !IsRunning;
}
