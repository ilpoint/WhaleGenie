using System;
using System.Diagnostics;
using System.IO;
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

    /// <summary>Whether the pin on the menu bar is holding the window above the other windows.</summary>
    private bool _alwaysOnTop;

    /// <summary>The shared hook the Home key is answered through; null until the window is open.</summary>
    private IGlobalHook? _hotkeyHook;

    /// <summary>Set by the tray once the answer to the leaving question is "go".</summary>
    private bool _allowClose;

    /// <summary>Whether the question about unsaved work is on screen, so only one is asked.</summary>
    private bool _askingToSave;

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

    public MainWindow()
    {
        InitializeComponent();

        AskToSaveProject = () => ConfirmDialog.ShowAsync(this,
            Strings.Get("Main.UnsavedTitle"),
            Strings.Get("Main.UnsavedMessage"),
            Strings.Get("Main.UnsavedSave"),
            Strings.Get("Main.UnsavedDiscard"));

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
            addMacroButton.Click += async (_, _) => await ShowMacroEditorAsync();
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
        base.OnClosing(e);

        if (_allowClose
            || !Leaving
            || DataContext is not MainViewModel { HasUnsavedChanges: true })
        {
            return;
        }

        e.Cancel = true;

        // Out of the closing event before the question is asked: the dialog is a window of its own,
        // and opening one while this one is being closed is not a position to be in.
        Dispatcher.UIThread.Post(() => _ = PromptToSaveProjectAsync());
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

            _allowClose = true;
            Close();
        }
        finally
        {
            _askingToSave = false;
        }
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
        Closed += (_, _) =>
        {
            DetachSystemHotkey();
            StopTriggers();
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

    /// <summary>Opens the macro editor, either for a new macro or for an existing one.</summary>
    private async Task ShowMacroEditorAsync(MacroItem? existing = null)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        // The package path is what a picture taken from the screen is stored beside.
        var editor = new MacroEditorWindow(existing, [.. viewModel.Macros], viewModel.CurrentPath);
        var macro = await editor.ShowDialogOver<MacroItem?>(this);

        if (macro is null)
        {
            return;
        }

        if (existing is null)
        {
            viewModel.AddMacro(macro);
            return;
        }

        // The edited macro replaces the one the trigger may be running.
        _triggers?.Stop(existing);
        viewModel.ReplaceMacro(existing, macro);
        RefreshRunning(viewModel);
    }

    private async void OnEditMacroClicked(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (sender is Control { DataContext: MacroItem macro })
        {
            await ShowMacroEditorAsync(macro);
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
