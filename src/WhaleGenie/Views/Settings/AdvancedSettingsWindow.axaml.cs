using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

/// <summary>
/// The native editors left after the settings pages moved to the web. They stay native because
/// they point at windows or draw the wheel with the painter the live wheel uses.
/// </summary>
public partial class AdvancedSettingsWindow : AppWindow
{
    private readonly SettingsViewModel _viewModel;
    private readonly FANavigationView _nav;
    private readonly SettingsTriggersView _triggers;
    private readonly ScrollViewer _scroll;
    private readonly Dictionary<SettingsSubPage, double> _pageOffsets = [];
    private SettingsSubPage _lastPage;
    private bool _wheelEditorReady;
    private bool _gateEntered;

    public AdvancedSettingsWindow()
        : this(null, null, SettingsSubPage.MenuLauncher)
    {
    }

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

    public AdvancedSettingsWindow(MainViewModel? project, string? packagePath,
        SettingsSubPage page = SettingsSubPage.MenuLauncher)
    {
        InitializeComponent();
        WindowChrome.Attach(this);

        _triggers = this.FindControl<SettingsTriggersView>("TriggerPages")!;
        _scroll = this.FindControl<ScrollViewer>("SettingsScroll")!;
        _lastPage = page;

        _viewModel = new SettingsViewModel(SharedDevices.Current.Driver,
            project: project, packagePath: packagePath);
        DataContext = _viewModel;
        Title = Strings.Get("Settings.Title");

        _triggers.CaptureMenuLauncherButton.Click += (_, _) =>
        {
            _viewModel.IsCapturingSystemHotkey = false;
            _viewModel.BeginMenuLauncherCapture();
        };
        _triggers.ClearMenuLauncherButton.Click += (_, _) => _viewModel.ClearMenuLauncher();

        _triggers.EditMenuRuleButton.IsEnabled = project is not null;
        _triggers.RemoveMenuRuleButton.IsEnabled = project is not null;

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

        _viewModel.SetPage(AdvancedPage(page));
        SelectRoot(_nav, _viewModel.SelectedPage);

        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.SelectedPage))
            {
                SelectRoot(_nav, _viewModel.SelectedPage);
                RememberScrollPosition();
                EnsureWheelEditor();
            }
        };

        EnsureWheelEditor();

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnPreviewKeyUp, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnPreviewPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerWheelChangedEvent, OnPreviewPointerWheel, RoutingStrategies.Tunnel);

        Closed += (_, _) => LeaveTriggerGate();
    }

    private static SettingsSubPage AdvancedPage(SettingsSubPage page)
        => page is SettingsSubPage.Theme
            ? page
            : page is SettingsSubPage.MenuLauncher or SettingsSubPage.WindowRules
                or SettingsSubPage.WheelMenu or SettingsSubPage.WheelActions
                ? page
                : SettingsSubPage.MenuLauncher;

    internal void SelectPage(SettingsSubPage page) => _viewModel.SetPage(AdvancedPage(page));

    private void RememberScrollPosition()
    {
        _pageOffsets[_lastPage] = _scroll.Offset.Y;
        _lastPage = _viewModel.SelectedPage;
        var target = _pageOffsets.GetValueOrDefault(_lastPage);
        Dispatcher.UIThread.Post(
            () => _scroll.Offset = new Vector(0, target),
            DispatcherPriority.Loaded);
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

    internal void OnPreviewWheel(object? sender, RoutedEventArgs e)
    {
        var origin = new PixelPoint(
            Position.X + (int)(Width / 2),
            Position.Y + (int)(Height / 2));
        var preview = new WheelMenuWindow(_viewModel.BuildWheelLayout(),
            _viewModel.WheelState.Options, origin, preview: true);
        preview.ShowOver(this);
    }
}
