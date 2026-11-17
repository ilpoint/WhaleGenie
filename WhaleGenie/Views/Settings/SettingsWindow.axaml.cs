using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

public partial class SettingsWindow : AppWindow
{
    private readonly SettingsViewModel _viewModel;
    private readonly FANavigationView _nav;
    private readonly SettingsSystemView _system;
    private readonly SettingsTriggersView _triggers;
    private bool _wheelEditorReady;
    private bool _gateEntered;

    public SettingsWindow()
        : this(null, null, SettingsSubPage.General)
    {
    }

    /// <summary>
    /// The trigger gate is held for as long as the window is on screen, not from the moment it is
    /// built: a settings window built for a page check and never shown must not keep every trigger
    /// held back for the rest of the session.
    /// </summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (!_gateEntered)
        {
            _gateEntered = true;
            MacroTriggerGate.Enter();
        }
    }

    private void LeaveTriggerGate()
    {
        if (_gateEntered)
        {
            _gateEntered = false;
            MacroTriggerGate.Exit();
        }
    }

    public SettingsWindow(MainViewModel? project, string? packagePath,
        SettingsSubPage page = SettingsSubPage.General)
    {
        InitializeComponent();
        WindowChrome.Attach(this);

        _system = this.FindControl<SettingsSystemView>("SystemPages")!;
        _triggers = this.FindControl<SettingsTriggersView>("TriggerPages")!;

        _viewModel = new SettingsViewModel(SharedDevices.Current.Driver,
            project: project, packagePath: packagePath);
        DataContext = _viewModel;
        Title = Strings.Get("Settings.Title");

        var doneButton = this.FindControl<Button>("DoneButton");
        if (doneButton is not null)
        {
            doneButton.Click += (_, _) => Close();
        }

        _system.CaptureSystemHotkeyButton.Click += (_, _) =>
        {
            _viewModel.StopMenuLauncherCapture();
            _viewModel.IsCapturingSystemHotkey = !_viewModel.IsCapturingSystemHotkey;
        };
        _system.ClearSystemHotkeyButton.Click += (_, _) =>
            _viewModel.ClearSystemHotkey();
        _triggers.CaptureMenuLauncherButton.Click += (_, _) =>
        {
            _viewModel.IsCapturingSystemHotkey = false;
            _viewModel.BeginMenuLauncherCapture();
        };
        _triggers.ClearMenuLauncherButton.Click += (_, _) =>
            _viewModel.ClearMenuLauncher();
        _system.CaptureRecordStopButton.Click += (_, _) =>
        {
            _viewModel.IsCapturingSystemHotkey = false;
            _viewModel.StopMenuLauncherCapture();
            _viewModel.IsCapturingRecordStop = !_viewModel.IsCapturingRecordStop;
        };

        _triggers.EditMenuRuleButton.IsEnabled = project is not null;
        _triggers.RemoveMenuRuleButton.IsEnabled = project is not null;

        // The pages are one list, and the group named at the top of the window decides which of them
        // the list shows. All that is left here is opening on the page the caller asked for.
        _nav = this.FindControl<FANavigationView>("Nav")!;

        var ruleList = _triggers.MenuRuleList!;
        var addPage = _triggers.AddMenuRulePageButton;
        addPage.IsEnabled = false;
        ruleList.SelectionChanged += (_, _) =>
            addPage.IsEnabled = ruleList.SelectedItem is MenuRuleRow { IsBlocked: false };
        ruleList.AddHandler(PointerPressedEvent, OnRulePointerPressed,
            RoutingStrategies.Tunnel);
        ruleList.AddHandler(PointerMovedEvent, OnRulePointerMoved,
            RoutingStrategies.Tunnel);
        ruleList.AddHandler(PointerReleasedEvent, OnRulePointerReleased,
            RoutingStrategies.Tunnel);

        _viewModel.SetPage(page);
        ShowMenuItemsOf(page);
        SelectPage(_nav, page);

        // A page can also change without the list being touched: the tabs at the top of the window,
        // or anything else that sets the page. Both paths end in the same two steps, so the list and
        // the page cannot end up naming different groups.
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.SelectedPage))
            {
                ShowMenuItemsOf(_viewModel.SelectedPage);
                SelectPage(_nav, _viewModel.SelectedPage);
                EnsureWheelEditor();
            }
        };

        // The wheel's sectors are drawn rather than templated, so the action manager builds its
        // own controls and shares the painter with the live wheel. Building it only when its page
        // is first opened keeps every other settings page cheap to open.
        EnsureWheelEditor();

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnPreviewKeyUp, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnPreviewPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerWheelChangedEvent, OnPreviewPointerWheel, RoutingStrategies.Tunnel);

        // Keys typed here are the interface's, not a macro's. The window is not modal any more, so
        // the hold lasts for as long as it is open — the same rule the editor and the run window
        // keep, and the reason the pages can capture a hotkey without setting one off.
        // Counted while the window is on screen, not while it is being built — a settings window
        // built for a page check and never shown must not hold the triggers back.
        Closed += (_, _) => LeaveTriggerGate();
    }

    private void EnsureWheelEditor()
    {
        if (_wheelEditorReady || _viewModel.SelectedPage != SettingsSubPage.WheelActions)
        {
            return;
        }

        if (_triggers.WheelEditorHost is { } host)
        {
            host.Content = new WheelMenuEditorView(_viewModel, this);
            _wheelEditorReady = true;
        }
    }

    internal async void OnCustomizeThemeClicked(object? sender, RoutedEventArgs e)
        => await new ThemePaletteWindow().ShowDialogOver(this);

    internal void OnCaptureWheelRepeat(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.IsCapturingWheelRepeat)
        {
            _viewModel.IsCapturingWheelRepeat = false;
        }
        else
        {
            _viewModel.BeginWheelRepeatCapture();
        }
    }

    internal void OnEnableSystem(object? sender, RoutedEventArgs e)
        => _viewModel.EnableSystem();

    /// <summary>
    /// Opens the wheel from the settings window so it can be seen and aimed at without any system
    /// input: preview mode answers the pointer inside Avalonia rather than through the global hook.
    /// </summary>
    internal void OnPreviewWheel(object? sender, RoutedEventArgs e)
    {
        var origin = new PixelPoint(
            Position.X + (int)(Width / 2),
            Position.Y + (int)(Height / 2));
        var preview = new WheelMenuWindow(_viewModel.BuildWheelLayout(),
            _viewModel.WheelState.Options, origin, preview: true);
        preview.ShowOver(this);
    }

    internal void OnOpenStorage(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: string folder } || folder.Length == 0)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }
        catch (Exception error)
        {
            // 界面只说一句本地化的话；打不开的理由（只读目录、被策略拦住）写进日志，
            // 把一串 .NET 文本贴到窗口里对谁都不算说明。
            AppDiagnostics.Record("settings.storage.open", error);
            if (_system.StorageProblem is { } problem)
            {
                problem.IsVisible = true;
            }
        }
    }

    internal void OnInstallDriverClicked(object? sender, RoutedEventArgs e)
        => WebPage.Open(ProjectLinks.DriverDownload);

    internal void OnRestartElevatedClicked(object? sender, RoutedEventArgs e)
    {
        SharedDevices.Current.Driver.HandOver();
        if (!ProcessRights.RestartElevated())
        {
            return;
        }

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

}
