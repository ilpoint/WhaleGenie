using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Recording;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

public partial class MacroEditorWindow : AppWindow
{
    // The documents open in the editor: the root macro, its sub-macros and the public ones added to
    // the project. A change to tabs or sub-macro calls does not need the step tree or trigger code.
    private void BuildDocumentPages()
    {
        _session.Documents.Clear();
        _documentPages.Clear();
        var current = new MacroDocumentPageViewModel(_rootViewModel, isCurrent: true);
        _session.Documents.Add(current);
        _documentPages.Add(current);

        var children = _rootViewModel.SubMacros.ToList();
        _rootViewModel.SubMacros.Clear();
        AddChildPages(children);
        _rootViewModel.Documents = _session.Documents;
        SettleDocumentIds();
        RefreshDocuments();
        SelectDocument(current);
    }

    private readonly List<MacroDocumentPageViewModel> _documentPages = [];

    private void AddChildPages(IEnumerable<MacroItem> children)
    {
        foreach (var child in children)
        {
            var nested = child.SubMacros.ToList();
            var editor = new MacroEditorViewModel { IsSubMacro = true, Persisted = _rootViewModel.Persisted };
            if (_devices is { } devices)
            {
                editor.Windows = devices.Windows;
            }

            editor.Documents = _session.Documents;
            editor.LoadFrom(child);
            editor.SubMacros.Clear();
            WatchChildEditor(editor);
            var page = new MacroDocumentPageViewModel(editor, isCurrent: false, child);
            _session.Documents.Add(page);
            _documentPages.Add(page);
            AddChildPages(nested);
        }
    }

    private void AddBlankSubMacro()
    {
        var editor = new MacroEditorViewModel { IsSubMacro = true, Persisted = _rootViewModel.Persisted };
        editor.Documents = _session.Documents;
        if (_devices is { } devices)
        {
            editor.Windows = devices.Windows;
        }

        editor.Name = UniqueSubMacroName(Strings.Get("Editor.NewSubMacroName"));
        WatchChildEditor(editor);
        AddChildPage(editor, null);
        SettleDocumentIds();
        SelectDocument(_documentPages[^1]);
        _rootViewModel.IsDirty = true;
        RefreshProjectCallables();
        RefreshDocuments();
        KeepRecovery();
    }

    private async void CopyProjectMacroAsSub()
    {
        var candidates = _session.Project
            .Where(macro => !ReferenceEquals(macro, _session.Editing))
            .ToList();
        if (await MenuMacroPickerWindow.ShowAsync(this, candidates,
                Strings.Get("Editor.PickSubMacro"),
                Strings.Get("Editor.PickSubMacroSearch"), enabledOnly: false) is not { } source)
        {
            return;
        }

        var copy = MacroItem.FromJson(source.ToSubMacroJson());
        copy.Origin = null;
        copy.Name = UniqueSubMacroName(source.Name);
        AddExistingSubMacro(copy);
    }

    /// <summary>
    /// Writes the page in front to a file of its own: the macro with everything that makes it
    /// callable, and the pictures it looks for. Triggers stay behind on purpose — they belong to
    /// the project that runs the macro, and the project it lands in gives it a way to be started.
    /// </summary>
    internal async void OnExportDocumentClicked(object? sender, RoutedEventArgs e)
    {
        var macro = _viewModel.IsSubMacro ? _viewModel.BuildMacro() : BuildRootSnapshot();
        var path = await PackagePicker.AskWhereAsync(this, Strings.Get("Editor.ExportTitle"),
            macro.Name);
        if (path is null)
        {
            return;
        }

        try
        {
            MacroPackage.Save(path, [macro], MacroBundle.UsedGlobals([macro]), AppVersion.Text,
                _session.AssetFolder);
        }
        catch (Exception error)
        {
            await ConfirmDialog.ShowAsync(this, Strings.Get("Editor.ExportTitle"),
                $"{Path.GetFileName(path)}\n{error.Message}", Strings.Get("Common.Ok"), null, false);
        }
    }

    private void AddExistingSubMacro(MacroItem macro)
    {
        var editor = new MacroEditorViewModel { IsSubMacro = true, Persisted = _rootViewModel.Persisted };
        editor.Documents = _session.Documents;
        if (_devices is { } devices)
        {
            editor.Windows = devices.Windows;
        }

        editor.LoadFrom(macro);
        editor.SubMacros.Clear();
        editor.IsDirty = true;
        WatchChildEditor(editor);
        AddChildPage(editor, macro);
        SettleDocumentIds();
        SelectDocument(_documentPages[^1]);
        _rootViewModel.IsDirty = true;
        RefreshProjectCallables();
        RefreshDocuments();
        KeepRecovery();
    }

    private void AddChildPage(MacroEditorViewModel editor, MacroItem? source)
    {
        var page = new MacroDocumentPageViewModel(editor, isCurrent: false, source);
        editor.Documents = _session.Documents;
        _session.Documents.Add(page);
        _documentPages.Add(page);
    }

    /// <summary>
    /// Gives every step of every open document an id of its own, and says on the page it happened
    /// to when one had to move: a submacro brought in from elsewhere arrives with the ids it was
    /// written with, and the caller it was dropped into may already be using them.
    /// </summary>
    private void SettleDocumentIds()
    {
        var renamed = StepIds.SettleAcross(_documentPages.Select(page => page.Editor.Steps));
        if (renamed.Count > 0)
        {
            _documentPages[^1].Editor.ShowStepIdsRepaired(renamed);
        }
    }

    private string UniqueSubMacroName(string basis)
    {
        var names = _documentPages
            .Select(page => page.Editor.Name.Trim())
            .Concat(_session.Project.Select(macro => macro.Name.Trim()))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stem = basis.Trim();
        if (stem.Length == 0)
        {
            stem = Strings.Get("Editor.NewSubMacroName");
        }

        if (names.Add(stem))
        {
            return stem;
        }

        for (var index = 2; ; index++)
        {
            var candidate = $"{stem} ({index})";
            if (names.Add(candidate))
            {
                return candidate;
            }
        }
    }

    private void WatchChildEditor(MacroEditorViewModel editor)
    {
        editor.SystemReader = new SystemVariableReader(Layer);
        _session.EditorNames[editor] = editor.Name;
        editor.CloseRequested += _ => SaveAll(closeAfterSave: false);
        editor.AddStepRequested += OnAddStepRequested;
        editor.EditStepRequested += OnEditStepRequested;
        editor.ClearRequested += OnClearRequested;
        editor.CopyRequested += () => _ = CopySelectionAsync();
        editor.CutRequested += () => _ = CutSelectionAsync();
        editor.PasteRequested += () => _ = PasteAsync();
        editor.RunRequested += OnRunRequested;
        editor.AdjustDelaysRequested += OnAdjustDelaysRequested;
        editor.RunMacroRequested += OnRunMacroRequested;
        editor.SaveBlocked += OnSaveBlocked;
        editor.MacroLifted += OnMacroLifted;
        editor.RecordingChanged += OnRecordingChanged;
        editor.VariableCenter.EditRequested += OnEditVariableRequested;
        editor.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MacroEditorViewModel.IsDirty)
                && editor.IsDirty)
            {
                _rootViewModel.IsDirty = true;
                KeepRecovery();
            }

            if (args.PropertyName == nameof(MacroEditorViewModel.Name))
            {
                var oldName = _session.EditorNames.GetValueOrDefault(editor, string.Empty);
                var newName = editor.Name.Trim();
                _session.EditorNames[editor] = editor.Name;
                if (oldName.Length > 0 && newName.Length > 0
                    && !string.Equals(oldName, newName, StringComparison.Ordinal))
                {
                    foreach (var page in _documentPages)
                    {
                        RenameCalls(page.Editor.Steps, oldName, newName);
                        page.Editor.NotifyStepsChanged();
                    }
                }

                RefreshProjectCallables();
                RefreshDocuments();
                RefreshPalette();
            }
        };
        editor.Changed += (_, _) =>
        {
            _settings.Find<TextBlock>("SavedNotice")!.IsVisible = false;
            _rootViewModel.IsDirty = true;
            RefreshDocuments();
            KeepRecovery();
        };
    }

    private IReadOnlyList<PublicMacroEntry> LoadPublicMacros()
    {
        try
        {
            return PublicMacroLibrary.Load(AppPaths.PublicMacroLibrary);
        }
        catch (Exception error)
        {
            _publicMacroLoadError =
                Strings.Format("PublicMacroManager.LoadFailed", error.Message);
            return [];
        }
    }

    private void RefreshProjectCallables()
    {
        var projectCalls = _session.Project
            .Where(macro => !ReferenceEquals(macro, _session.Editing))
            .Select(macro => macro.Callable)
            .ToList();
        var sharedChildren = _documentPages
            .Where(page => !page.IsCurrent)
            .Select(page => page.Editor.BuildMacro().Callable)
            .ToList();
        var browserShared = OtherMacroUsesBrowser();

        // The submacros of the document being edited are callable too, and they were nowhere in
        // this list: the picker beside a call step could not name one, and a step that did name one
        // was reported as a call to a macro the project does not have. They are gathered by the
        // same rule the run uses, so a name that works here works there.
        var subCalls = CallableMacros.Callables(DocumentSubMacros());

        foreach (var page in _documentPages)
        {
            var ownName = page.Editor.Name.Trim();
            var calls = projectCalls
                .Concat(sharedChildren)
                .Concat(subCalls)
                .Where(call => !string.Equals(call.Name.Trim(), ownName,
                    StringComparison.OrdinalIgnoreCase))
                .GroupBy(call => call.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            page.Editor.SetProjectMacros(calls, browserShared);
        }
    }

    private void RefreshDocuments()
    {
        foreach (var page in _documentPages)
        {
            page.HasProblem = !page.Editor.CanSave;
        }

        RefreshPalette();
    }

    private void RefreshPalette()
    {
        // Every submacro the document holds, not only the ones already called: the dock is where
        // one is picked to be called, so it has to show the one that was just written and saved.
        _palette.SetSources(_viewModel.InsertChoices, DocumentSubMacros(), _publicMacros);
    }

    /// <summary>
    /// Every submacro of the document being edited, as the pages hold them: the root editor hands
    /// its own list over to the pages when the window opens, so the pages are where a submacro
    /// lives while it is being written.
    /// </summary>
    private IReadOnlyList<MacroItem> DocumentSubMacros()
        =>
        [
            .. _documentPages
                .Where(page => !page.IsCurrent)
                .Select(page => page.Editor.BuildMacro())
                .Where(macro => macro.Name.Trim().Length > 0),
        ];

    /// <summary>The name a call writes for one submacro: its own when that is unique, else 父/子.</summary>
    private string CallNameFor(MacroItem sub)
        => CallableMacros.Named(DocumentSubMacros())
            .FirstOrDefault(pair => string.Equals(pair.Macro.Name, sub.Name,
                StringComparison.OrdinalIgnoreCase)) is { Name: { } name }
            ? name
            : sub.Name;

    private void SelectDocument(MacroDocumentPageViewModel page)
    {
        _switchingDocuments = true;
        try
        {
            _viewModel = page.Editor;
            DataContext = page.Editor;
            _viewModel.Documents = _session.Documents;
            var tabs = this.FindControl<ListBox>("DocumentTabs");
            if (tabs is not null)
            {
                tabs.SelectedItem = page;
            }

            EnsureInterface();
            RefreshPalette();
            _viewModel.RefreshSelection();
        }
        finally
        {
            _switchingDocuments = false;
        }
    }

    private void SwitchToSubMacro(MacroItem macro)
    {
        var page = _documentPages.FirstOrDefault(candidate =>
            ReferenceEquals(candidate.Source, macro)
            || (!candidate.IsCurrent
                && string.Equals(candidate.Editor.Name, macro.Name,
                    StringComparison.OrdinalIgnoreCase)));
        if (page is not null)
        {
            if (!_session.Documents.Contains(page))
            {
                _session.Documents.Add(page);
            }

            SelectDocument(page);
        }
    }

    internal void OnDocumentTabChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_switchingDocuments
            || sender is not ListBox { SelectedItem: MacroDocumentPageViewModel page })
        {
            return;
        }

        SelectDocument(page);
    }

    internal void OnDocumentTabClosePressed(object? sender, PointerPressedEventArgs e)
    {
        _pageBeforeTabClose = _documentPages.FirstOrDefault(candidate =>
            ReferenceEquals(candidate.Editor, _viewModel));
    }

    internal void OnCloseDocumentTab(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: MacroDocumentPageViewModel page } || page.IsCurrent)
        {
            return;
        }

        var active = _pageBeforeTabClose
            ?? _documentPages.FirstOrDefault(candidate => ReferenceEquals(candidate.Editor, _viewModel));
        _pageBeforeTabClose = null;
        var closingActive = ReferenceEquals(active, page);

        _session.Documents.Remove(page);
        SelectDocument(closingActive ? _documentPages[0] : active ?? _documentPages[0]);
        RefreshDocuments();
    }

    private async void ManagePublicMacros()
    {
        _publicMacros = await PublicMacroLibraryWindow.ShowAsync(this, _publicMacros,
            _publicMacroLoadError);
        _publicMacroLoadError = null;
        RefreshPalette();
    }

    internal async void OnDeleteSubMacro(object? sender, RoutedEventArgs e)
    {
        var page = _documentPages.FirstOrDefault(candidate =>
            !candidate.IsCurrent && ReferenceEquals(candidate.Editor, _viewModel));
        if (page is null)
        {
            return;
        }

        var callers = _documentPages
            .Where(candidate => Calls(candidate.Editor.Steps, page.Editor.Name))
            .Select(candidate => candidate.Title)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (callers.Count > 0)
        {
            await ConfirmDialog.ShowAsync(this,
                Strings.Get("Editor.SubMacroInUseTitle"),
                Strings.Format("Editor.SubMacroInUse", string.Join(" / ", callers)),
                Strings.Get("Common.Ok"),
                showCancel: false);
            return;
        }

        var choice = await ConfirmDialog.ShowAsync(this,
            Strings.Get("Editor.DeleteSubMacroTitle"),
            Strings.Format("Editor.DeleteSubMacroMessage", page.Editor.Name),
            Strings.Get("Editor.DeleteSubMacro"),
            cancelLabel: Strings.Get("Common.Cancel"));
        if (choice != ConfirmChoice.Primary)
        {
            return;
        }

        _session.Documents.Remove(page);
        _documentPages.Remove(page);
        SelectDocument(_documentPages[0]);
        _rootViewModel.IsDirty = true;
        RefreshProjectCallables();
        RefreshDocuments();
        KeepRecovery();
    }

    private static bool Calls(IEnumerable<MacroStep> steps, string macroName)
    {
        foreach (var step in steps)
        {
            if (step.Type is "control.runMacro"
                && string.Equals(step.Text("macro").Trim(), macroName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (step.StepLists.Any(parameter => Calls(parameter.Steps, macroName)))
            {
                return true;
            }
        }

        return false;
    }

    private static void RenameCalls(IEnumerable<MacroStep> steps, string oldName, string newName)
    {
        foreach (var step in steps)
        {
            if (step.Type is "control.runMacro"
                && step.Parameters.FirstOrDefault(parameter => parameter.Name == "macro")
                    is { } target
                && string.Equals(target.Value?.Trim(), oldName,
                    StringComparison.OrdinalIgnoreCase))
            {
                target.Written = Formula.Text(newName);
            }

            foreach (var parameter in step.StepLists)
            {
                RenameCalls(parameter.Steps, oldName, newName);
            }
        }
    }

    private bool SaveAll(bool closeAfterSave)
    {
        RefreshProjectCallables();
        var bad = _documentPages.FirstOrDefault(page => !page.Editor.CanSave);
        if (bad is not null)
        {
            SelectDocument(bad);
            OnSaveBlocked();
            return false;
        }

        var result = BuildRootSnapshot();
        Result = result;
        foreach (var page in _documentPages)
        {
            page.Editor.IsDirty = false;
        }

        _rootViewModel.IsDirty = false;
        _mustStayUnsaved = false;
        StopRecovery();
        Saved?.Invoke(result);
        _settings.Find<TextBlock>("SavedNotice")!.IsVisible = true;

        if (closeAfterSave)
        {
            _closing = true;
            _allowClose = true;
            Close(result);
        }

        return true;
    }
}
