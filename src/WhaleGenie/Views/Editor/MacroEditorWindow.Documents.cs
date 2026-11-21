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
        var current = _documentPages.Build();
        RefreshDocuments();
        SelectDocument(current);
    }

    private readonly EditorDocumentPages _documentPages;

    /// <summary>Which page stands for a macro, and which one is in front after a tab closes.</summary>
    private readonly EditorTabs _tabs;

    private void AddBlankSubMacro()
    {
        var page = _documentPages.AddBlank();
        SelectDocument(page);
        NoteEdit();
        RefreshProjectCallables();
        RefreshDocuments();
        _recovery.Keep();
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
        copy.Name = _documentPages.UniqueName(source.Name);
        AddExistingSubMacro(copy);
    }

    /// <summary>
    /// Writes the page in front to a file of its own: the macro with everything that makes it
    /// callable, and the pictures it looks for. Triggers stay behind on purpose — they belong to
    /// the project that runs the macro, and the project it lands in gives it a way to be started.
    /// </summary>
    internal async void OnExportDocumentClicked(object? sender, RoutedEventArgs e)
    {
        var draft = _viewModel.IsSubMacro
            ? new MacroDraft(_viewModel.BuildMacro(), [], [])
            : _viewModel.Draft();
        var macro = draft.Macro;
        var path = await PackagePicker.AskWhereAsync(this, Strings.Get("Editor.ExportTitle"),
            macro.Name);
        if (path is null)
        {
            return;
        }

        try
        {
            var triggers = new ProjectTriggers();
            foreach (var entry in draft.Entries)
            {
                triggers.Entries.Add(entry.Copy());
            }

            foreach (var layout in draft.Layouts)
            {
                triggers.InterfaceLayouts.Add(layout);
            }

            MacroPackage.Save(path, [macro],
                MacroBundle.Take(triggers, [macro]),
                MacroBundle.UsedGlobals([macro], _variables.Globals), AppVersion.Text,
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
        var page = _documentPages.AddExisting(macro);
        SelectDocument(page);
        NoteEdit();
        RefreshProjectCallables();
        RefreshDocuments();
        _recovery.Keep();
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
                NoteEdit();
                _recovery.Keep();
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
                        EditorDocumentNames.RenameCalls(page.Editor.Steps, oldName, newName);
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
            NoteEdit();
            RefreshDocuments();
            _recovery.Keep();
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
        if (_tabs.Find(macro) is not { } page)
        {
            return;
        }

        if (!_session.Documents.Contains(page))
        {
            _session.Documents.Add(page);
        }

        SelectDocument(page);
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
        => _tabs.NotePress(_tabs.PageOf(_viewModel));

    internal void OnCloseDocumentTab(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: MacroDocumentPageViewModel page } || page.IsCurrent)
        {
            return;
        }

        var front = _tabs.AfterClose(page, _tabs.PageOf(_viewModel));
        _documentPages.Close(page);
        SelectDocument(front);
        RefreshDocuments();
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
            .Where(candidate => EditorDocumentNames.Calls(candidate.Editor.Steps, page.Editor.Name))
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

        _documentPages.Forget(page);
        SelectDocument(_documentPages[0]);
        NoteEdit();
        RefreshProjectCallables();
        RefreshDocuments();
        _recovery.Keep();
    }
}
