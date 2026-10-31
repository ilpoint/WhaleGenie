using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    private MenuRuleRow? _ruleDragSource;
    private Point _ruleDragOrigin;
    private bool _ruleDragging;

    public SettingsWindow()
        : this(null, null, SettingsRootPage.System, SettingsSubPage.General)
    {
    }

    public SettingsWindow(MainViewModel? project, string? packagePath,
        SettingsRootPage root = SettingsRootPage.System,
        SettingsSubPage sub = SettingsSubPage.General)
    {
        InitializeComponent();

        _viewModel = new SettingsViewModel(SharedDevices.Current.Driver,
            project: project, packagePath: packagePath);
        DataContext = _viewModel;
        Title = Strings.Get("Settings.Title");

        var closeButton = this.FindControl<Button>("CloseButton");
        if (closeButton is not null)
        {
            closeButton.Click += (_, _) => Close();
        }

        var doneButton = this.FindControl<Button>("DoneButton");
        if (doneButton is not null)
        {
            doneButton.Click += (_, _) => Close();
        }

        this.FindControl<Button>("CaptureSystemHotkeyButton")!.Click += (_, _) =>
        {
            _viewModel.StopMenuLauncherCapture();
            _viewModel.IsCapturingSystemHotkey = !_viewModel.IsCapturingSystemHotkey;
        };
        this.FindControl<Button>("ClearSystemHotkeyButton")!.Click += (_, _) =>
            _viewModel.ClearSystemHotkey();
        this.FindControl<Button>("CaptureMenuLauncherButton")!.Click += (_, _) =>
        {
            _viewModel.IsCapturingSystemHotkey = false;
            _viewModel.BeginMenuLauncherCapture();
        };
        this.FindControl<Button>("ClearMenuLauncherButton")!.Click += (_, _) =>
            _viewModel.ClearMenuLauncher();

        this.FindControl<Button>("EditMenuRuleButton")!.IsEnabled = project is not null;
        this.FindControl<Button>("RemoveMenuRuleButton")!.IsEnabled = project is not null;

        // The three roots are a tab control now: picking one is the selection, and the binding on
        // its SelectedIndex writes the same enum a SetPage call would. The sub-pages inside a root
        // are still buttons, because they share one tab's content area.
        this.FindControl<Button>("GeneralNavButton")!.Click += (_, _) =>
            _viewModel.SetPage(SettingsRootPage.System, SettingsSubPage.General);
        this.FindControl<Button>("CaptureNavButton")!.Click += (_, _) =>
            _viewModel.SetPage(SettingsRootPage.System, SettingsSubPage.CaptureInput);
        this.FindControl<Button>("DevicesNavButton")!.Click += (_, _) =>
            _viewModel.SetPage(SettingsRootPage.System, SettingsSubPage.Devices);
        this.FindControl<Button>("MenuLauncherNavButton")!.Click += (_, _) =>
            _viewModel.SetPage(SettingsRootPage.Triggers, SettingsSubPage.MenuLauncher);
        this.FindControl<Button>("WindowRulesNavButton")!.Click += (_, _) =>
            _viewModel.SetPage(SettingsRootPage.Triggers, SettingsSubPage.WindowRules);
        this.FindControl<Button>("ThemeNavButton")!.Click += (_, _) =>
            _viewModel.SetPage(SettingsRootPage.Appearance, SettingsSubPage.Theme);

        var ruleList = this.FindControl<ListBox>("MenuRuleList")!;
        var addPage = this.FindControl<Button>("AddMenuRulePageButton")!;
        addPage.IsEnabled = false;
        ruleList.SelectionChanged += (_, _) =>
            addPage.IsEnabled = ruleList.SelectedItem is MenuRuleRow { IsBlocked: false };
        ruleList.AddHandler(PointerPressedEvent, OnRulePointerPressed,
            RoutingStrategies.Tunnel);
        ruleList.AddHandler(PointerMovedEvent, OnRulePointerMoved,
            RoutingStrategies.Tunnel);
        ruleList.AddHandler(PointerReleasedEvent, OnRulePointerReleased,
            RoutingStrategies.Tunnel);

        _viewModel.SetPage(root, sub);
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnPreviewKeyUp, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnPreviewPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerWheelChangedEvent, OnPreviewPointerWheel, RoutingStrategies.Tunnel);
        Closed += (_, _) => MacroTriggerGate.Exit();
        MacroTriggerGate.Enter();
    }

    private async void OnCustomizeThemeClicked(object? sender, RoutedEventArgs e)
        => await new ThemePaletteWindow().ShowDialogOver(this);

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel.IsCapturingSystemHotkey)
        {
            if (HotkeyNameMapper.IsModifier(e.Key))
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                _viewModel.IsCapturingSystemHotkey = false;
                e.Handled = true;
                return;
            }

            _viewModel.CaptureSystemHotkey(Chord(e.Key, e.KeyModifiers));
            e.Handled = true;
            return;
        }

        if (_viewModel.IsCapturingMenuLauncher)
        {
            if (HotkeyNameMapper.IsModifier(e.Key))
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                _viewModel.StopMenuLauncherCapture();
                e.Handled = true;
                return;
            }

            _viewModel.CaptureMenuLauncher(Chord(e.Key, e.KeyModifiers));
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void OnPreviewKeyUp(object? sender, KeyEventArgs e)
    {
        if (_viewModel.IsCapturingHotkey && HotkeyNameMapper.IsModifier(e.Key))
        {
            e.Handled = true;
        }
    }

    private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_viewModel.IsCapturingMenuLauncher)
        {
            return;
        }

        // A press on the buttons of the hotkey row is the user cancelling or clearing, not a
        // mouse button to record. The handler runs on the way down, before the button sees the
        // click, so swallowing it here left the capture impossible to stop with the mouse.
        if (IsHotkeyButton(e.Source))
        {
            return;
        }

        var button = e.GetCurrentPoint(this).Properties.PointerUpdateKind switch
        {
            PointerUpdateKind.LeftButtonPressed => HookMouseButton.Left,
            PointerUpdateKind.RightButtonPressed => HookMouseButton.Right,
            PointerUpdateKind.MiddleButtonPressed => HookMouseButton.Middle,
            PointerUpdateKind.XButton1Pressed => HookMouseButton.Back,
            PointerUpdateKind.XButton2Pressed => HookMouseButton.Forward,
            _ => (HookMouseButton?)null,
        };
        if (button is not { } pressed)
        {
            return;
        }

        _viewModel.CaptureMenuLauncher(KeyNames.MouseName(pressed));
        e.Handled = true;
    }

    /// <summary>Whether a press landed on one of the buttons that set, cancel or clear a hotkey.</summary>
    private static bool IsHotkeyButton(object? source)
    {
        var names = new[]
        {
            "CaptureSystemHotkeyButton", "ClearSystemHotkeyButton",
            "CaptureMenuLauncherButton", "ClearMenuLauncherButton",
        };

        for (var visual = source as Visual; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is Button button && button.Name is { } name
                && names.Contains(name, StringComparer.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private void OnPreviewPointerWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!_viewModel.IsCapturingMenuLauncher)
        {
            return;
        }

        var (axis, rotation) = System.Math.Abs(e.Delta.X) > System.Math.Abs(e.Delta.Y)
            ? (HookWheelAxis.Horizontal, e.Delta.X < 0 ? 1 : -1)
            : (HookWheelAxis.Vertical, e.Delta.Y >= 0 ? 1 : -1);
        _viewModel.CaptureMenuLauncher(KeyNames.WheelName(axis, rotation));
        e.Handled = true;
    }

    private async Task AddMenuRuleAsync()
    {
        if (await WindowRuleWindow.PickAsync(this, SharedDevices.Current.Windows,
            SharedDevices.Current.Hook) is { } rule)
        {
            _viewModel.AddMenuRule(rule);
        }
    }

    private async Task EditMenuRuleAsync()
    {
        if (this.FindControl<ListBox>("MenuRuleList")?.SelectedItem is not MenuRuleRow row
            || row.IsDefault)
        {
            return;
        }

        if (await WindowRuleWindow.PickAsync(this, SharedDevices.Current.Windows,
            SharedDevices.Current.Hook, row.Rule) is { } rule)
        {
            _viewModel.UpdateMenuRule(row, rule);
        }
    }

    private void OnAddMenuRule(object? sender, RoutedEventArgs e) => _ = AddMenuRuleAsync();

    private void OnEditMenuRule(object? sender, RoutedEventArgs e) => _ = EditMenuRuleAsync();

    private void OnRemoveMenuRule(object? sender, RoutedEventArgs e)
    {
        if (this.FindControl<ListBox>("MenuRuleList")?.SelectedItem is MenuRuleRow row)
        {
            _viewModel.RemoveMenuRule(row);
        }
    }

    private void OnAddMenuRulePage(object? sender, RoutedEventArgs e)
    {
        if (this.FindControl<ListBox>("MenuRuleList")?.SelectedItem is MenuRuleRow row)
        {
            _viewModel.AddMenuPage(row);
        }
    }

    private void OnMoveMenuRuleUp(object? sender, RoutedEventArgs e)
        => MoveMenuRule(-1);

    private void OnMoveMenuRuleDown(object? sender, RoutedEventArgs e)
        => MoveMenuRule(1);

    private void MoveMenuRule(int step)
    {
        if (this.FindControl<ListBox>("MenuRuleList")?.SelectedItem is MenuRuleRow row)
        {
            _viewModel.MoveMenuRule(row, step);
        }
    }

    private void OnRulePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not ListBox list
            || !e.GetCurrentPoint(list).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _ruleDragSource = RowAt(e.Source as Visual);
        _ruleDragOrigin = e.GetPosition(list);
        _ruleDragging = false;
    }

    private void OnRulePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_ruleDragSource is null || sender is not ListBox list
            || !e.GetCurrentPoint(list).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var point = e.GetPosition(list);
        _ruleDragging = Math.Abs(point.X - _ruleDragOrigin.X) > 5
                        || Math.Abs(point.Y - _ruleDragOrigin.Y) > 5;
    }

    private void OnRulePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not ListBox list || !_ruleDragging || _ruleDragSource is not { } source)
        {
            _ruleDragSource = null;
            _ruleDragging = false;
            return;
        }

        var target = RowAt(list.InputHitTest(e.GetPosition(list)) as Visual);
        _ruleDragSource = null;
        _ruleDragging = false;
        if (target is not null)
        {
            _viewModel.MoveMenuRuleTo(source, target);
        }
    }

    private static MenuRuleRow? RowAt(Visual? visual)
    {
        var item = visual?.FindAncestorOfType<ListBoxItem>(true);
        return item?.DataContext as MenuRuleRow;
    }

    private static string Chord(Key key, KeyModifiers held)
        => HotkeyNameMapper.Chord(key, held);

    private void OnInstallDriverClicked(object? sender, RoutedEventArgs e)
        => WebPage.Open(ProjectLinks.DriverDownload);

    private void OnRestartElevatedClicked(object? sender, RoutedEventArgs e)
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

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            && e.GetPosition(this).Y <= 38)
        {
            BeginMoveDrag(e);
        }
    }
}
