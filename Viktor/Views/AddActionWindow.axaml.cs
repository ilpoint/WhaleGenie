using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Viktor.Core.Devices.Platform;
using Viktor.Models;
using Viktor.ViewModels;

namespace Viktor.Views;

public partial class AddActionWindow : Window
{
    public AddActionWindow()
        : this(null, null, null, null, null)
    {
    }

    /// <summary>
    /// Opens the dialog, optionally preloaded with a step being edited and optionally
    /// restricted to a catalogue subset, such as the condition actions.
    /// </summary>
    public AddActionWindow(MacroStep? existing, IReadOnlyList<ActionDefinition>? actions = null,
        IReadOnlyList<string>? variables = null, IReadOnlyList<string>? macros = null,
        string? presetKey = null)
    {
        InitializeComponent();

        var viewModel = new AddActionViewModel(actions, variables, macros);
        if (existing is not null)
        {
            viewModel.LoadFrom(existing);
        }
        else if (!string.IsNullOrEmpty(presetKey))
        {
            viewModel.SelectAction(presetKey);
        }

        DataContext = viewModel;
        viewModel.CloseRequested += Close;
        viewModel.NestedAddRequested += OnNestedAddRequested;
        viewModel.NestedEditRequested += OnNestedEditRequested;
        Title = viewModel.Header;

        var minimizeButton = this.FindControl<Button>("MinimizeButton");
        if (minimizeButton is not null)
        {
            minimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;
        }

        var closeButton = this.FindControl<Button>("CloseButton");
        if (closeButton is not null)
        {
            closeButton.Click += (_, _) => Close(null);
        }

        // Tunnelling so Escape cancels before any focused editor consumes it.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

        // A variable picker shows nothing until a key is pressed, so open its candidate
        // list as soon as the field is clicked or receives focus.
        AddHandler(GotFocusEvent, OnVariablePickerFocused, RoutingStrategies.Bubble);
        AddHandler(PointerPressedEvent, OnVariablePickerPressed, RoutingStrategies.Tunnel);
    }

    private void OnVariablePickerFocused(object? sender, RoutedEventArgs e) => OpenCandidates(e.Source);

    private void OnVariablePickerPressed(object? sender, PointerPressedEventArgs e) => OpenCandidates(e.Source);

    /// <summary>Opens the dropdown of a variable picker that has something to offer.</summary>
    private static void OpenCandidates(object? source)
    {
        if (source is not Visual visual
            || visual.FindAncestorOfType<AutoCompleteBox>(true) is not { } picker
            || picker.ItemsSource is not { } items
            || !items.Cast<object>().Any())
        {
            return;
        }

        picker.IsDropDownOpen = true;
    }

    /// <summary>Opens a picker for a nested list and appends whatever the user builds.</summary>
    private async void OnNestedAddRequested(StepListEditorViewModel list)
    {
        var variables = (DataContext as AddActionViewModel)?.CollectVariables();
        var dialog = new AddActionWindow(null, list.Catalog, variables);
        var step = await dialog.ShowDialog<MacroStep?>(this);

        if (step is not null)
        {
            list.AddStep(step);
        }
    }

    /// <summary>Opens a picker for an existing nested step and swaps in the result.</summary>
    private async void OnNestedEditRequested(MacroStep step)
    {
        var variables = (DataContext as AddActionViewModel)?.CollectVariables();
        var dialog = new AddActionWindow(step, null, variables);
        var edited = await dialog.ShowDialog<MacroStep?>(this);

        if (edited is null || DataContext is not AddActionViewModel viewModel)
        {
            return;
        }

        foreach (var parameter in viewModel.Parameters)
        {
            if (parameter.List is { } list && list.Steps.Contains(step))
            {
                list.ReplaceStep(step, edited);
                return;
            }
        }
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        // Alt + X takes the pointer's place, for the actions that aim at a screen position.
        if (e.Key == Key.X && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            e.Handled = true;
            CaptureCursorPosition();
            return;
        }

        if (e.Key is not Key.Escape || DataContext is not AddActionViewModel viewModel)
        {
            return;
        }

        viewModel.CancelCommand.Execute(null);
        e.Handled = true;
    }

    private void CaptureCursorPosition()
    {
        if (DataContext is AddActionViewModel viewModel)
        {
            var point = WindowsScreenDevice.CursorPosition();
            viewModel.ApplyCursorPosition(point.X, point.Y);
        }
    }

    /// <summary>Opens the region picker and writes the rectangle it returns into the action.</summary>
    private async void OnPickRegion(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AddActionViewModel viewModel)
        {
            return;
        }

        var region = await RegionPickerWindow.PickAsync(this);
        if (region is { } picked)
        {
            viewModel.ApplyRegion(picked.X, picked.Y, picked.Width, picked.Height);
        }
    }

    /// <summary>Switches a number between the spinner and an expression such as $match.x.</summary>
    private void OnToggleNumberFormula(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: StepParameterViewModel parameter })
        {
            parameter.ToggleFormula();
        }
    }

    /// <summary>Opens the screen magnifier for a colour parameter and keeps what it reads.</summary>
    private async void OnPickParameterColor(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: StepParameterViewModel parameter })
        {
            return;
        }

        var colour = await ColorPickerWindow.PickAsync(this);
        if (colour is { } picked)
        {
            parameter.Text = picked.ToHex();
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
