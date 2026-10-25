using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

/// <summary>
/// The one window an action is written in. The macro editor opens it after a card is dropped, the
/// run window opens it for a breakpoint condition, and pickers reuse the same form rather than
/// growing a second action editor.
/// </summary>
public class ActionEditorWindow : Window
{
    private readonly ActionEditorView _view;

    public ActionEditorWindow()
        : this(null, null, null, null, null, null)
    {
    }

    public ActionEditorWindow(MacroStep? existing, IReadOnlyList<ActionDefinition>? actions = null,
        IReadOnlyList<VariableChoice>? variables = null, IReadOnlyList<MacroCallable>? macros = null,
        string? presetKey = null, string? assetFolder = null,
        IReadOnlyList<ActionParameterOption>? steps = null, string? newStepId = null)
    {
        Title = Strings.Get("Editor.ActionEditor");
        Width = 1024;
        Height = 800;
        MinWidth = 800;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = -1;
        WindowDecorations = WindowDecorations.BorderOnly;
        Background = (IBrush?)Application.Current?.Resources["AppBackdrop"] ?? Brushes.Transparent;

        _view = new ActionEditorView(existing, actions, variables, macros, presetKey, assetFolder,
            steps, newStepId);
        Content = _view;
        DataContext = _view.DataContext;
        _view.Finished += step => Close(step);
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

        if (_view.FindControl<Button>("MinimizeButton") is { } minimize)
        {
            minimize.Click += (_, _) => WindowState = WindowState.Minimized;
        }

        if (_view.FindControl<Button>("CloseButton") is { } close)
        {
            close.Click += (_, _) => Close(null);
        }
    }

    /// <summary>
    /// Opens at a size the desktop can hold, beside the window that asked for it where there is
    /// room on that side. The form itself no longer owns a window, so the wrapper is where the
    /// window-sized decisions stay.
    /// </summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        _view.WatchPreview();

        var owner = Owner;
        var screen = (owner is not null ? Screens.ScreenFromWindow(owner) : null) ?? Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var scale = screen.Scaling > 0 ? screen.Scaling : 1;
        var desktop = (X: screen.WorkingArea.X / scale, Y: screen.WorkingArea.Y / scale,
            Width: screen.WorkingArea.Width / scale, Height: screen.WorkingArea.Height / scale);
        var (width, height) = DialogSize.Fit(desktop.Width, desktop.Height);

        if (owner is Window editor
            && DialogSize.Beside(ActionEditorView.EditorRect(editor), desktop, height) is { } beside)
        {
            Width = beside.Width;
            Height = height;
            Position = new PixelPoint(
                (int)Math.Round(beside.X * scale), (int)Math.Round(beside.Y * scale));
            return;
        }

        Width = width;
        Height = height;
    }
    internal IDeviceLayer Devices
    {
        get => _view.Devices;
        set => _view.Devices = value;
    }

    internal IMacroLibrary? Macros
    {
        get => _view.Macros;
        set => _view.Macros = value;
    }

    internal bool StaysOpen
    {
        get => _view.StaysOpen;
        set => _view.StaysOpen = value;
    }

    internal bool ShowPicker
    {
        set => _view.ShowPicker = value;
    }

    internal void SetMacroCall(string macroName)
    {
        _view.SelectAction("control.runMacro");
        if (DataContext is AddActionViewModel form
            && form.Parameters.FirstOrDefault(parameter =>
                parameter.Definition.Name == "macro") is { } target)
        {
            target.Text = macroName;
        }
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        // A form that is waiting for a key takes every key of the window, Esc and Alt+X included:
        // those are keys a step may send, and clicking the button again is how a capture ends.
        if (_view.IsCapturingKey)
        {
            return;
        }

        if (DataContext is not AddActionViewModel form)
        {
            return;
        }

        if (e.Key == Key.X && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            var cursor = _view.Devices.Screen.Cursor;
            form.ApplyCursorPosition(cursor.X, cursor.Y);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            form.CancelCommand.Execute(null);
            e.Handled = true;
        }
    }

    internal Func<MacroStep?, MacroStep, string?>? PlaceStep
    {
        get => _view.PlaceStep;
        set => _view.PlaceStep = value;
    }

    internal Func<string>? MintStepId
    {
        get => _view.MintStepId;
        set => _view.MintStepId = value;
    }

    internal Action<MacroStep>? LookAtStep
    {
        get => _view.LookAtStep;
        set => _view.LookAtStep = value;
    }

    internal IReadOnlyCollection<long> FramedWindows => _view.FramedWindows;

    internal void BeginStep(string? presetKey) => _view.BeginStep(presetKey);

    internal void EditStep(MacroStep step) => _view.EditStep(step);

    internal void Retarget(IReadOnlyList<ActionDefinition>? actions,
        IReadOnlyList<VariableChoice>? variables, IReadOnlyList<MacroCallable>? macros,
        IReadOnlyList<ActionParameterOption>? steps, string? assetFolder)
        => _view.Retarget(actions, variables, macros, steps, assetFolder);
}
