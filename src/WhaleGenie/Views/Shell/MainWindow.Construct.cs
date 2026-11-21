using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;


namespace WhaleGenie.Views;

// The two ways the window is made, and the state an open editor gives back.
public partial class MainWindow : AppWindow
{
    public MainWindow()
        : this(null)
    {
    }

    /// <summary>
    /// The triggers, built when something first needs them. A window that is only read for its
    /// bindings should not put a key hook on the machine; the program hands in the one the container
    /// built, so opening the window never builds a second.
    /// </summary>
    private TriggerRuntime Runtime => _runtime ??= new TriggerRuntime(_devices,
        (DataContext as MainViewModel)?.Variables ?? new VariableSession());

    /// <summary>
    /// The window over the run's own triggers. The program hands in the one the container built, so
    /// the service that starts a run is the same one the window shows the state of.
    /// </summary>
    internal MainWindow(TriggerRuntime? runtime, IDeviceLayer? devices = null, SystemState? system = null,
        RunQuestionHost? questions = null)
    {
        _runtime = runtime;
        _system = system ?? new SystemState();
        _questions = questions ?? new RunQuestionHost();

        // The runtime already names the layer it watches; a caller that has no runtime — a check —
        // hands its own in, and the program-wide layer answers only when neither did.
        _devices = devices ?? runtime?.Devices ?? SharedDevices.Current;
        InitializeComponent();

        // 标题栏归库，但这一扇的内容伸进标题栏：那条 40px 的行里放着置顶钮，整条都可以拖动。
        // 系统按钮由库画在它上面，行尾留出了它们的宽度。
        TitleBar.ExtendsContentIntoTitleBar = true;
        TitleBar.Height = 40;

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

        var addMacroButton = this.FindControl<Button>("AddMacroButton");
        if (addMacroButton is not null)
        {
            addMacroButton.Click += (_, _) => OpenEditor(null, null, draft: false,
                (DataContext as MainViewModel)?.SelectedView);
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

        var runDebugButton = this.FindControl<Button>("RunDebugButton");
        if (runDebugButton is not null)
        {
            runDebugButton.Click += async (_, _) => await ShowRunDebugAsync();
        }

        var alwaysOnTopButton = this.FindControl<Button>("AlwaysOnTopButton");
        if (alwaysOnTopButton is not null)
        {
            alwaysOnTopButton.Click += (_, _) => ToggleAlwaysOnTop(alwaysOnTopButton);
        }
    }
}
