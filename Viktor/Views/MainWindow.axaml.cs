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
using Viktor.Core.Devices.Platform;
using Viktor.Core.Execution;
using Viktor.Execution;
using Viktor.Localization;
using Viktor.Models;
using Viktor.Storage;
using Viktor.ViewModels;

namespace Viktor.Views;

public partial class MainWindow : Window
{
    /// <summary>Runs the macros the list is waiting for; null until the window is open.</summary>
    private MacroTriggerService? _triggers;

    /// <summary>Whether the pin on the menu bar is holding the window above the other windows.</summary>
    private bool _alwaysOnTop;

    /// <summary>The shared hook the Home key is answered through; null until the window is open.</summary>
    private IGlobalHook? _hotkeyHook;

    public MainWindow()
    {
        InitializeComponent();

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

        // A key Viktor sent itself is its own output, not the user asking for a switch.
        if (ViktorInputGate.RecentlySent(KeyNames.Name(e.Data.KeyCode, sided: true)))
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
        var macros = (DataContext as MainViewModel)?.Macros;
        var window = new VariableCenterWindow(macros);
        await window.ShowDialog(this);
    }

    // -------------------------------------------------------------- macro package

    private static FilePickerFileType PackageFileType
        => new(Strings.Get("Package.Filter")) { Patterns = ["*.vkm"] };

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

    private async Task SaveAsAsync(MainViewModel viewModel)
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
            Save(viewModel, path);
        }
    }

    private void Save(MainViewModel viewModel, string path)
    {
        try
        {
            viewModel.SavePackage(path);
        }
        catch (Exception error)
        {
            _ = ReportAsync(Strings.Get("Package.SaveFailed"), path, error);
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
    private async Task<StepErrorChoice> AskAboutFailedStep(string step, string reason, string detail)
        => await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var choice = await ConfirmDialog.ShowAsync(this, Strings.Get("Run.AskTitle"),
                FailedStepPrompt.Question(step, reason, detail),
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

    private async void OnOpenSettings(object? sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow();
        await dialog.ShowDialog(this);
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
        var macro = await editor.ShowDialog<MacroItem?>(this);

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
