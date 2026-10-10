using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SharpHook;
using SharpHook.Data;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Core.Execution;
using WhaleGenie.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

public partial class MainWindow : Window
{
    /// <summary>Runs the macros the list is waiting for; null until the window is open.</summary>
    private MacroTriggerService? _triggers;

    /// <summary>
    /// The macro editor on screen, or null while there is none. One at a time, because two of them
    /// on one macro would hand back two versions of it and the last one closed would win.
    /// </summary>
    private MacroEditorWindow? _editor;

    /// <summary>Whether the pin on the menu bar is holding the window above the other windows.</summary>
    private bool _alwaysOnTop;

    /// <summary>The shared hook the Home key is answered through; null until the window is open.</summary>
    private IGlobalHook? _hotkeyHook;

    /// <summary>Set by the tray once the answer to the leaving question is "go".</summary>
    private bool _allowClose;

    /// <summary>Whether the question about unsaved work is on screen, so only one is asked.</summary>
    private bool _askingToSave;

    /// <summary>Writes the unsaved project out as it changes; null until the window is watched.</summary>
    private RecoveryWriter? _recovery;

    /// <summary>
    /// Whether this session put the project into the snapshot. What clears it is what wrote it:
    /// the list comes up empty on every start, so clearing "because it is not dirty" at startup
    /// would throw away the last run's work before the user was even asked about it.
    /// </summary>
    private bool _projectInRecovery;

    /// <summary>
    /// Whether this run has already put the question about work the last one left behind. The
    /// window is announced again every time it comes back from the notification area, which it does
    /// whenever the macro editor closes, and what the snapshot holds by then is the work this run is
    /// already showing rather than the leftovers the question is about.
    /// </summary>
    private bool _recoveryOffered;

    /// <summary>
    /// The icon in the notification area, or null when this machine has none. It is what tells a
    /// close that leaves the program apart from a close that only puts the window out of the way,
    /// which is the difference between asking about unsaved work and saying nothing at all.
    /// </summary>
    internal AppTray? Tray { get; set; }

    /// <summary>
    /// Puts the question about the unsaved project to the user and reports what they chose. It is
    /// a property so that a check can answer without a dialog standing between it and the decision;
    /// nothing else replaces it.
    /// </summary>
    internal Func<Task<ConfirmChoice>> AskToSaveProject { get; set; }

    /// <summary>
    /// Offers back the work a stopped run left behind and reports what the user chose. A property
    /// for the same reason as <see cref="AskToSaveProject"/>: a check can answer without a dialog.
    /// </summary>
    internal Func<RecoveryContents, Task<ConfirmChoice>> AskToRecover { get; set; }

    public MainWindow()
    {
        InitializeComponent();

        AskToSaveProject = () => ConfirmDialog.ShowAsync(this,
            Strings.Get("Main.UnsavedTitle"),
            Strings.Get("Main.UnsavedMessage"),
            Strings.Get("Main.UnsavedSave"),
            Strings.Get("Main.UnsavedDiscard"));

        AskToRecover = contents => ConfirmDialog.ShowAsync(this,
            Strings.Get("Recover.Title"),
            RecoveryMessage(contents),
            Strings.Get("Recover.Restore"),
            Strings.Get("Recover.Discard"),
            height: 280);

        var minimizeButton = this.FindControl<Button>("MinimizeButton");
        if (minimizeButton is not null)
        {
            minimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;
        }

        var closeButton = this.FindControl<Button>("CloseButton");
        if (closeButton is not null)
        {
            closeButton.Click += (_, _) => Close();
        }

        var addMacroButton = this.FindControl<Button>("AddMacroButton");
        if (addMacroButton is not null)
        {
            addMacroButton.Click += (_, _) => OpenEditor(null, null, draft: false);
        }

        // Clicking a macro card arms or disarms it; the card buttons opt out below.
        var macroList = this.FindControl<ItemsControl>("MacroList");
        if (macroList is not null)
        {
            macroList.AddHandler(PointerPressedEvent, OnMacroCardPressed, RoutingStrategies.Bubble);
        }

        var settingsButton = this.FindControl<Button>("SettingsButton");
        if (settingsButton is not null)
        {
            settingsButton.Click += OnOpenSettings;
        }

        var variableCenterButton = this.FindControl<Button>("VariableCenterButton");
        if (variableCenterButton is not null)
        {
            variableCenterButton.Click += async (_, _) => await ShowVariableCenterAsync();
        }

        var alwaysOnTopButton = this.FindControl<Button>("AlwaysOnTopButton");
        if (alwaysOnTopButton is not null)
        {
            alwaysOnTopButton.Click += (_, _) => ToggleAlwaysOnTop(alwaysOnTopButton);
        }
    }

    /// <summary>Keeps the main window above the other windows until the pin is pressed again.</summary>
    private void ToggleAlwaysOnTop(Button button)
    {
        _alwaysOnTop = !_alwaysOnTop;
        Topmost = _alwaysOnTop;

        // Lights the button up and swaps its hint, so the state is readable at a glance.
        button.Classes.Set("Pinned", _alwaysOnTop);
        ToolTip.SetTip(button, Strings.Get(_alwaysOnTop ? "Main.AlwaysOnTopCancel" : "Main.AlwaysOnTop"));
    }

    /// <summary>True when a close really stops the program rather than hiding the window.</summary>
    private bool Leaving => Tray is null || Tray.IsLeaving;

    /// <summary>
    /// Asks about the project before the program stops. A plain close is not the moment for this:
    /// the window only goes out of the way, and everything in it is still there to be saved later.
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_allowClose
            && Leaving
            && DataContext is MainViewModel { HasUnsavedChanges: true })
        {
            // The answer is given before the base call, because the base call is what raises the
            // Closing event and the way out reads the answer there. A handler that ran first and
            // found nothing would take this window for one that had already gone, and would stop
            // the program on top of the very question it is asking.
            e.Cancel = true;

            // And the question itself goes up one turn later: the dialog is a window of its own,
            // and opening one while this one is being closed is not a position to be in.
            Dispatcher.UIThread.Post(() => _ = PromptToSaveProjectAsync());
        }

        base.OnClosing(e);
    }

    /// <summary>
    /// Puts the unsaved project to the user, the same question the macro editor asks about one
    /// macro. Only "save" and "discard" let the program stop; dismissing the question means the
    /// user is staying, and the way out the tray started is called off with it.
    /// </summary>
    private async Task PromptToSaveProjectAsync()
    {
        if (_askingToSave || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        _askingToSave = true;
        try
        {
            // The window is usually away in the notification area, and a question nobody can see
            // is no question at all: it comes back so the answer can be given.
            if (!IsVisible)
            {
                Show();
                WindowState = WindowState.Normal;
                Activate();
            }

            var choice = await AskToSaveProject();

            if (choice == ConfirmChoice.Primary)
            {
                // A save that was called off — the file picker dismissed — is an answer as well:
                // the project is still unsaved, so the program stays.
                if (!await SaveProjectAsync(viewModel))
                {
                    Tray?.CalledOff();
                    return;
                }
            }
            else if (choice != ConfirmChoice.Secondary)
            {
                Tray?.CalledOff();
                return;
            }

            // The program is going with nothing carried over: the project was just written out,
            // or the user said the unsaved work is not wanted, so the snapshot has no business
            // surviving to be offered back on the next run.
            RecoveryStore.ClearProject();

            _allowClose = true;
            Close();
        }
        finally
        {
            _askingToSave = false;
        }
    }

    /// <summary>
    /// Keeps the recovery snapshot in step with the macro list: written while there is something
    /// to lose, dropped the moment there is not. A run that ends without notice — a crash, or the
    /// machine going down — therefore leaves the work somewhere the next run can find it.
    /// </summary>
    private void WatchUnsavedProject(MainViewModel viewModel)
    {
        _recovery = new RecoveryWriter(() => WriteUnsavedProject(viewModel));

        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainViewModel.IsDirty)
                or nameof(MainViewModel.HasUnsavedChanges)
                or nameof(MainViewModel.CurrentPath))
            {
                SyncUnsavedProject(viewModel);
            }
        };

        // A change that arrives while the list is already unsaved reaches nothing above: the dirty
        // flag does not move for it, so the snapshot would be left holding the state it had before.
        viewModel.Changed += (_, _) => KeepUnsavedProject(viewModel);

        // The armed state of a macro is written into the package, so a change to it counts, and
        // it moves nothing that would raise a property change on the list itself.
        viewModel.Macros.CollectionChanged += (_, _) => KeepUnsavedProject(viewModel);
        SyncUnsavedProject(viewModel);
    }

    /// <summary>Writes the project out when it first becomes unsaved, and lets the snapshot go
    /// when there is nothing left to lose.</summary>
    private void SyncUnsavedProject(MainViewModel viewModel)
    {
        if (!viewModel.HasUnsavedChanges)
        {
            _recovery?.Stop();

            // Only take back what this session put there. A snapshot left by the last run is not
            // this window's to drop: the list is empty on every start, and clearing it here would
            // do that before the recovered work had even been offered.
            if (_projectInRecovery)
            {
                _projectInRecovery = false;
                RecoveryStore.ClearProject();
            }

            return;
        }

        _projectInRecovery = true;
        _recovery?.Now();
    }

    /// <summary>
    /// Keeps a change that arrived while the list was already unsaved from going unwritten. The
    /// write itself waits for the changes to pause; what makes the work unsaved was written already.
    /// </summary>
    private void KeepUnsavedProject(MainViewModel viewModel)
    {
        if (!viewModel.HasUnsavedChanges)
        {
            return;
        }

        _projectInRecovery = true;
        _recovery?.Changed();
    }

    private static void WriteUnsavedProject(MainViewModel viewModel)
    {
        if (!viewModel.HasUnsavedChanges)
        {
            return;
        }

        RecoveryStore.SaveProject([.. viewModel.Macros], [.. VariableCatalog.Globals],
            viewModel.CurrentPath);
    }

    /// <summary>
    /// Starts watching for the keys the macros are bound to. The system switch on the main window
    /// turns the watching on and off, so this only has to follow it.
    /// </summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (_triggers is not null || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        _triggers = new MacroTriggerService(() => viewModel.Macros);
        _triggers.Ask = AskAboutFailedStep;
        _triggers.StatusChanged += OnTriggerStatusChanged;
        _triggers.IsEnabled = viewModel.IsRunning;

        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.IsRunning) && _triggers is not null)
            {
                _triggers.IsEnabled = viewModel.IsRunning;
            }
        };

        RefreshRunning(viewModel);
        AttachSystemHotkey();
        WatchUnsavedProject(viewModel);
        Closed += (_, _) =>
        {
            DetachSystemHotkey();
            StopTriggers();
            _recovery?.Stop();
        };
    }

    /// <summary>
    /// Answers the Home key from anywhere, so the system switch works without bringing the window
    /// forward. The hook is the one the trigger service already keeps alive for the process.
    /// </summary>
    private void AttachSystemHotkey()
    {
        try
        {
            var hook = GlobalInputHook.Shared.Hook;
            hook.KeyPressed += OnSystemHotkeyPressed;
            _hotkeyHook = hook;
        }
        catch (Exception)
        {
            // Without a hook the button on the window still works.
            _hotkeyHook = null;
        }
    }

    private void DetachSystemHotkey()
    {
        if (_hotkeyHook is { } hook)
        {
            hook.KeyPressed -= OnSystemHotkeyPressed;
            _hotkeyHook = null;
        }
    }

    private void OnSystemHotkeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        // The editor and the other dialogs own the keyboard while they are open.
        if (e.Data.KeyCode != KeyCode.VcHome || MacroTriggerGate.IsOpen)
        {
            return;
        }

        // A key WhaleGenie sent itself is its own output, not the user asking for a switch.
        if (WhaleGenieInputGate.RecentlySent(KeyNames.Name(e.Data.KeyCode, sided: true)))
        {
            return;
        }

        Dispatcher.UIThread.Post(ToggleSystemFromHotkey);
    }

    /// <summary>Flips the system switch, unless a field on the window has the keyboard.</summary>
    private void ToggleSystemFromHotkey()
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (IsActive && FocusManager?.GetFocusedElement() is TextBox)
        {
            return;
        }

        // A macro bound to Home keeps the key for itself; the button still switches the system.
        foreach (var macro in viewModel.Macros)
        {
            if (macro.IsEnabled
                && macro.TriggerMode == MacroTrigger.KeystrokesButtonInputs
                && string.Equals(macro.BindKey.Trim(), "Home", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        viewModel.IsRunning = !viewModel.IsRunning;
    }

    /// <summary>Whether the pressed key should answer a macro, rather than the app being edited.</summary>
    private void OnTriggerStatusChanged()
        => Dispatcher.UIThread.Post(() => RefreshRunning(DataContext as MainViewModel));

    /// <summary>Marks the macros the trigger is running, which colours the dots on their cards.</summary>
    private void RefreshRunning(MainViewModel? viewModel)
    {
        if (_triggers is null || viewModel is null)
        {
            return;
        }

        foreach (var macro in viewModel.Macros)
        {
            macro.IsRunning = _triggers.IsRunning(macro);
        }
    }

    private void StopTriggers()
    {
        if (_triggers is null)
        {
            return;
        }

        _triggers.StatusChanged -= OnTriggerStatusChanged;
        _triggers.Dispose();
        _triggers = null;
    }

    /// <summary>Opens the Variable Center, listing the variables the macros can use.</summary>
    private async Task ShowVariableCenterAsync()
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        // The shared variables are written into the package with the macros, so adding or editing
        // one is a change to the project like any other — and nothing on the macro list moves when
        // it happens, so this is the one place that can notice it.
        var before = MainViewModel.SharedVariables();
        await new VariableCenterWindow(viewModel.Macros).ShowDialogOver(this);

        if (MainViewModel.SharedVariablesChangedSince(before))
        {
            viewModel.MarkSharedVariablesChanged();
        }
    }

    // -------------------------------------------------------------- macro package

    // -------------------------------------------------------------- recovery

    /// <summary>
    /// Offers back whatever the last run left unsaved. The list comes up empty, so this is the one
    /// moment the snapshot can be read: the work it holds is not in any package yet. It is asked
    /// once for the whole run — when the real application starts, and directly by a check.
    /// </summary>
    internal async Task OfferRecoveryAsync()
    {
        if (_recoveryOffered || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        _recoveryOffered = true;

        if (RecoveryStore.Load() is not { } contents)
        {
            return;
        }

        switch (await AskToRecover(contents))
        {
            case ConfirmChoice.Primary:
                Recover(viewModel, contents);
                break;
            case ConfirmChoice.Secondary:
                // The work is not wanted, so it goes for good rather than being asked about
                // again on the next run.
                RecoveryStore.Clear();
                break;
            default:
                // Put off, not answered: the snapshot stays, so the next run asks again.
                break;
        }
    }

    private void Recover(MainViewModel viewModel, RecoveryContents contents)
    {
        viewModel.RestoreFrom(contents);
        RefreshRunning(viewModel);

        // An editor that was open is opened again on the macro it was holding, so that work comes
        // back too and not only the list behind it.
        if (contents.Editor is { } editor)
        {
            var target = editor.Replaces is { Length: > 0 } name
                ? viewModel.Macros.FirstOrDefault(macro =>
                    string.Equals(macro.Name, name, StringComparison.OrdinalIgnoreCase))
                : null;

            OpenEditor(editor.Macro, target, draft: true);
        }
    }

    /// <summary>Says what the snapshot holds, one place at a time, for the question.</summary>
    private static string RecoveryMessage(RecoveryContents contents)
    {
        var found = new List<string>();
        if (contents.Macros.Count > 0 || contents.PackagePath is not null)
        {
            found.Add(Strings.Format("Recover.Project", contents.Macros.Count));
        }

        if (contents.Editor is not null)
        {
            found.Add(Strings.Get("Recover.Editor"));
        }

        return Strings.Format("Recover.Message", string.Join(Environment.NewLine, found));
    }

    private static FilePickerFileType PackageFileType
        => new(Strings.Get("Package.Filter")) { Patterns = [$"*{MacroPackage.Extension}"] };

    private async void OnOpenProjectClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var path = await PickPackageAsync(Strings.Get("Main.OpenProjectTitle"));
        if (path is null)
        {
            return;
        }

        try
        {
            viewModel.OpenPackage(path);
        }
        catch (Exception error)
        {
            await ReportAsync(Strings.Get("Package.OpenFailed"), path, error);
        }
    }

    private async void OnImportProjectClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var path = await PickPackageAsync(Strings.Get("Main.ImportProjectTitle"));
        if (path is null)
        {
            return;
        }

        try
        {
            viewModel.ImportPackage(path);
        }
        catch (Exception error)
        {
            await ReportAsync(Strings.Get("Package.OpenFailed"), path, error);
        }
    }

    private async void OnSaveProjectClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (viewModel.CurrentPath is { Length: > 0 } current)
        {
            Save(viewModel, current);
            return;
        }

        await SaveAsAsync(viewModel);
    }

    private async void OnSaveProjectAsClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            await SaveAsAsync(viewModel);
        }
    }

    private async void OnReloadCurrentClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { CurrentPath: { Length: > 0 } path } viewModel)
        {
            return;
        }

        try
        {
            viewModel.OpenPackage(path);
        }
        catch (Exception error)
        {
            await ReportAsync(Strings.Get("Package.OpenFailed"), path, error);
        }
    }

    private void OnOpenLocationClicked(object? sender, RoutedEventArgs e)
        => OpenFolder((DataContext as MainViewModel)?.CurrentFolder);

    private void OnOpenDirectoryClicked(object? sender, RoutedEventArgs e)
        => OpenFolder((DataContext as MainViewModel)?.CurrentFolder
            ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

    private async void OnCloseAllClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { HasMacros: true } viewModel)
        {
            return;
        }

        var choice = await ConfirmDialog.ShowAsync(this, Strings.Get("Main.CloseAllTitle"),
            Strings.Get("Main.CloseAllMessage"), Strings.Get("Main.CloseAll"));

        if (choice == ConfirmChoice.Primary)
        {
            _triggers?.StopAll();
            viewModel.ClearMacros();
            viewModel.CurrentPath = null;
            RefreshRunning(viewModel);
        }
    }

    private async Task<string?> PickPackageAsync(string title)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [PackageFileType],
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    /// <summary>
    /// Writes the project out, asking where it goes when it has no file yet. False when the save
    /// was called off or failed, which is the answer that keeps a program from stopping on a
    /// project it did not manage to write.
    /// </summary>
    private async Task<bool> SaveProjectAsync(MainViewModel viewModel)
        => viewModel.CurrentPath is { Length: > 0 } current
            ? Save(viewModel, current)
            : await SaveAsAsync(viewModel);

    private async Task<bool> SaveAsAsync(MainViewModel viewModel)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.Get("Main.SaveProjectTitle"),
            SuggestedFileName = viewModel.CurrentPath is { Length: > 0 } current
                ? Path.GetFileName(current)
                : $"macros{MacroPackage.Extension}",
            DefaultExtension = MacroPackage.Extension.TrimStart('.'),
            FileTypeChoices = [PackageFileType],
            ShowOverwritePrompt = true,
        });

        if (file?.TryGetLocalPath() is { Length: > 0 } path)
        {
            return Save(viewModel, path);
        }

        return false;
    }

    private bool Save(MainViewModel viewModel, string path)
    {
        try
        {
            viewModel.SavePackage(path);
            return true;
        }
        catch (Exception error)
        {
            _ = ReportAsync(Strings.Get("Package.SaveFailed"), path, error);
            return false;
        }
    }

    /// <summary>Reports a package error without leaving the main window.</summary>
    private async Task ReportAsync(string header, string path, Exception error)
    {
        await ConfirmDialog.ShowAsync(this, header,
            $"{Path.GetFileName(path)}\n{error.Message}", Strings.Get("Common.Ok"), null, false);
    }

    /// <summary>
    /// Puts a failed step of a triggered macro to the user, the same question the debugger asks.
    /// A trigger runs its macro on a thread of its own, so the question has to be handed to the
    /// interface thread; an answer of "stop" is what a closed box or a cancelled run gives, which
    /// is the one choice that cannot make things worse on its own.
    /// </summary>
    private async Task<StepErrorChoice> AskAboutFailedStep(string step, string reason, string detail,
        string comment)
        => await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var choice = await ConfirmDialog.ShowAsync(this, Strings.Get("Run.AskTitle"),
                FailedStepPrompt.Question(step, reason, detail, comment),
                Strings.Get("Run.AskRetry"), Strings.Get("Run.AskSkip"), true, Strings.Get("Run.AskStop"));

            return choice switch
            {
                ConfirmChoice.Primary => StepErrorChoice.Retry,
                ConfirmChoice.Secondary => StepErrorChoice.Skip,
                _ => StepErrorChoice.Stop,
            };
        });

    private static void OpenFolder(string? folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return;
        }

        Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
    }

    private void OnSettingsClicked(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        OnOpenSettings(sender, e);
    }

    /// <summary>Shows what the whole release list says, which is the change log.</summary>
    private void OnWhatsNewClicked(object? sender, RoutedEventArgs e)
        => WebPage.Open(ProjectLinks.Releases);

    /// <summary>
    /// Opens where a newer version would be. WhaleGenie does not check by itself: asking GitHub on
    /// every launch is a network call nobody asked for, and opening the page leaves the choice —
    /// and the download — with the user.
    /// </summary>
    private void OnUpdatesClicked(object? sender, RoutedEventArgs e)
        => WebPage.Open(ProjectLinks.LatestRelease);

    private void OnFeedbackClicked(object? sender, RoutedEventArgs e)
        => WebPage.Open(ProjectLinks.Feedback);

    /// <summary>
    /// Says what the program is and offers the two documents that belong with it: the project page,
    /// and the list of what inside it was made by somebody else. Both the "?" button and "About"
    /// open this.
    /// </summary>
    private async void OnAboutClicked(object? sender, RoutedEventArgs e)
    {
        var choice = await ConfirmDialog.ShowAsync(this, Strings.Get("Main.Title"),
            Strings.Format("Main.AboutText", Version()), Strings.Get("Main.AboutOpenPage"),
            Strings.Get("Main.AboutNotices"), showCancel: false, height: 320);

        if (choice == ConfirmChoice.Primary)
        {
            WebPage.Open(ProjectLinks.Home);
        }
        else if (choice == ConfirmChoice.Secondary)
        {
            await ShowThirdPartyNoticesAsync();
        }
    }

    /// <summary>Shows the third-party work the release ships, which the same document lists.</summary>
    private async Task ShowThirdPartyNoticesAsync()
    {
        await ConfirmDialog.ShowAsync(this, Strings.Get("Main.AboutNotices"),
            ThirdPartyNotices.Read(), Strings.Get("Common.Ok"), showCancel: false, height: 460);
    }

    /// <summary>The version the about box names, read from the program itself.</summary>
    private static string Version()
        => typeof(MainWindow).Assembly.GetName().Version is { } version
            ? $"v{version.Major}.{version.Minor}.{version.Build}"
            : Strings.Get("Main.AboutUnknownVersion");

    private async void OnOpenSettings(object? sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow();
        await dialog.ShowDialogOver(this);
    }

    /// <summary>
    /// Opens the macro editor on a macro: one from the list, a new one (<paramref name="editing"/> is
    /// null), or a draft recovered from a run that stopped without notice. What that macro replaces
    /// when it is saved is <paramref name="existing"/>, which is null for a new one. It answers the
    /// editor back so that a check can write in it; nothing else reads that.
    /// </summary>
    internal MacroEditorWindow? OpenEditor(MacroItem? editing, MacroItem? existing, bool draft)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return null;
        }

        // Two editors on one macro would each hand back their own version and the last one closed
        // would win, so the editor already open is the one wanted; it is brought to the front.
        if (_editor is { } open)
        {
            open.Activate();
            return open;
        }

        // The package path is what a picture taken from the screen is stored beside.
        var editor = new MacroEditorWindow(editing, [.. viewModel.Macros], viewModel.CurrentPath);

        // Recovered work is unsaved by definition, so closing that editor has to ask about it rather
        // than let it go without a word.
        if (draft)
        {
            editor.MarkUnsaved();
        }

        _editor = editor;
        editor.Closed += (_, _) => OnEditorClosed(editor, existing, viewModel);

        // The list is what the editor was opened from, and on a screen with a game on it the list is
        // one more thing in the way and one more window that takes the editor down with it when it
        // is minimized. It steps aside for the while instead, and the icon brings it back.
        editor.SteppedAside = Tray?.StepAside() ?? false;
        editor.ShowAsPeer(this);
        return editor;
    }

    /// <summary>
    /// Takes what the editor ended with: the macro joins the list, or the one that was edited
    /// replaces it. Then the list comes back, unless the user is on their way out of the program.
    /// </summary>
    private void OnEditorClosed(MacroEditorWindow editor, MacroItem? existing, MainViewModel viewModel)
    {
        _editor = null;

        if (editor.SteppedAside && !(Tray?.IsLeaving ?? false))
        {
            Tray?.ComeBack();
        }

        if (editor.Result is not { } macro)
        {
            return;
        }

        if (existing is null)
        {
            viewModel.AddMacro(macro);
            WriteUnsavedProject(viewModel);
            return;
        }

        // An editor opened on a macro and brought back saying the same thing is not a change, and
        // must not leave the project with something to save: opening a macro to read it and closing
        // the editor again would otherwise write a snapshot, and the next run would offer back work
        // that was never edited — while the window is away in the notification area, with nothing
        // on screen to say there is anything unsaved.
        if (Same(existing, macro))
        {
            return;
        }

        // The edited macro replaces the one the trigger may be running.
        _triggers?.Stop(existing);
        viewModel.ReplaceMacro(existing, macro);
        RefreshRunning(viewModel);

        // The editor has just let its draft go, so the list is the only copy of this macro: it
        // goes into the snapshot now rather than waiting for the next turn of the timer.
        WriteUnsavedProject(viewModel);
    }

    /// <summary>
    /// Whether the editor brought back the very same macro. The editor builds the macro again as it
    /// hands it over, so this is a question about what the macro says rather than about which object
    /// it is; two macros that read the same are written to the package the same way.
    /// </summary>
    private static bool Same(MacroItem one, MacroItem other)
        => string.Equals(one.ToJson().ToJsonString(), other.ToJson().ToJsonString(),
            StringComparison.Ordinal);

    private void OnEditMacroClicked(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (sender is Control { DataContext: MacroItem macro })
        {
            OpenEditor(macro, macro, draft: false);
        }
    }

    private async void OnDeleteMacroClicked(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (sender is not Control { DataContext: MacroItem macro }
            || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var choice = await ConfirmDialog.ShowAsync(this, Strings.Get("Editor.DeleteTitle"),
            Strings.Format("Editor.DeleteMessage", macro.Name), Strings.Get("Editor.Delete"));

        if (choice == ConfirmChoice.Primary)
        {
            _triggers?.Stop(macro);
            viewModel.RemoveMacro(macro);
            RefreshRunning(viewModel);
        }
    }

    /// <summary>Clicking anywhere on a macro card toggles whether that macro is armed.</summary>
    private void OnMacroCardPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.Source is Visual source && source.FindAncestorOfType<Button>(true) is not null)
        {
            return;   // the card's own Edit and Delete buttons
        }

        if (e.Source is Visual clicked
            && clicked.FindAncestorOfType<Border>(true) is { DataContext: MacroItem macro }
            && DataContext is MainViewModel viewModel)
        {
            viewModel.ToggleMacro(macro);

            // Arming a macro again gives a "trigger once" macro a fresh chance to run.
            if (macro.IsEnabled)
            {
                _triggers?.Rearm(macro);
            }
            else
            {
                _triggers?.Stop(macro);
            }

            RefreshRunning(viewModel);
            e.Handled = true;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            && e.GetPosition(this).Y <= 36)
        {
            BeginMoveDrag(e);
        }
    }
}
