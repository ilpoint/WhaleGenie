using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

public partial class AddActionWindow : Window
{
    /// <summary>Whether the dialog has been dismissed, which can happen while a page is open.</summary>
    private bool _closed;

    public AddActionWindow()
        : this(null, null, null, null, null, null)
    {
    }

    /// <summary>
    /// Opens the dialog, optionally preloaded with a step being edited and optionally
    /// restricted to a catalogue subset, such as the condition actions.
    /// </summary>
    public AddActionWindow(MacroStep? existing, IReadOnlyList<ActionDefinition>? actions = null,
        IReadOnlyList<string>? variables = null, IReadOnlyList<string>? macros = null,
        string? presetKey = null, string? assetFolder = null)
    {
        InitializeComponent();

        var viewModel = new AddActionViewModel(actions, variables, macros);
        viewModel.AssetFolder = assetFolder ?? string.Empty;

        // Picking a position has to know where the window a step is anchored to sits right now,
        // so what the pointer is over is stored the way the engine will read it back. The same goes
        // for a step anchored to a control, which needs the control's own rectangle.
        viewModel.Windows = new WindowsWindowDevice();
        viewModel.Ui = new FlaUiDevice();

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
        var assets = (DataContext as AddActionViewModel)?.AssetFolder;
        var dialog = new AddActionWindow(null, list.Catalog, variables, null, null, assets);
        var step = await dialog.ShowDialogOver<MacroStep?>(this);

        if (step is not null)
        {
            list.AddStep(step);
        }
    }

    /// <summary>Opens a picker for an existing nested step and swaps in the result.</summary>
    private async void OnNestedEditRequested(MacroStep step)
    {
        var variables = (DataContext as AddActionViewModel)?.CollectVariables();
        var assets = (DataContext as AddActionViewModel)?.AssetFolder;
        var dialog = new AddActionWindow(step, null, variables, null, null, assets);
        var edited = await dialog.ShowDialogOver<MacroStep?>(this);

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

    /// <summary>
    /// Opens the expression builder on the field that asked for it and keeps what it returns.
    /// Writing a formula is the same whether the field holds an expression or a number turned
    /// into one, so both reach it through here.
    /// </summary>
    private async void OnOpenExpressionBuilder(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: StepParameterViewModel parameter })
        {
            return;
        }

        if (await ExpressionBuilderWindow.ShowFor(this, parameter.Text ?? string.Empty,
                parameter.Variables) is { } built)
        {
            parameter.Text = built;
        }
    }

    /// <summary>Folds one group of the action picker open or shut.</summary>
    private void OnToggleActionGroup(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ActionGroupViewModel group }
            && DataContext is AddActionViewModel viewModel)
        {
            viewModel.ToggleGroup(group);
        }
    }

    /// <summary>Chooses the action whose card was clicked.</summary>
    private void OnPickAction(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ActionDefinition definition }
            && DataContext is AddActionViewModel viewModel)
        {
            viewModel.SelectAction(definition.Key);
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

    /// <summary>Chooses a picture file for an image parameter.</summary>
    private async void OnBrowseImage(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: StepParameterViewModel parameter })
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.Get("Add.BrowseImageTitle"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(Strings.Get("Add.ImageFilter"))
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif"],
                },
            ],
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { Length: > 0 } path)
        {
            parameter.Text = path;
        }
    }

    /// <summary>
    /// Drags a rectangle on the screen and keeps it as a picture inside the macro project, which
    /// is how a reference image is normally made.
    /// </summary>
    private async void OnCaptureImage(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: StepParameterViewModel parameter }
            || DataContext is not AddActionViewModel viewModel)
        {
            return;
        }

        if (await RegionPickerWindow.PickAsync(this) is not { } region)
        {
            return;
        }

        try
        {
            parameter.Text = ImageAssets.Capture(new WindowsScreenDevice(),
                region.X, region.Y, region.Width, region.Height, viewModel.AssetFolder);
        }
        catch (Exception)
        {
            await ConfirmDialog.ShowAsync(this, Strings.Get("Add.CaptureImage"),
                Strings.Get("Add.ImageFailed"), Strings.Get("Common.Ok"), showCancel: false);
        }
    }

    /// <summary>Forgets the picture an image parameter points at.</summary>
    private void OnClearImage(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: StepParameterViewModel parameter })
        {
            parameter.Text = string.Empty;
        }
    }

    /// <summary>Picks one of the windows that are open and writes its title into the field.</summary>
    private async void OnPickWindow(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: StepParameterViewModel parameter })
        {
            return;
        }

        if (await WindowPickerWindow.PickAsync(this) is { Length: > 0 } title)
        {
            parameter.Text = title;
        }
    }

    /// <summary>
    /// Takes the control the pointer is put on through UI Automation and writes the selector it
    /// reads. The window that control sits in fills the action's window filter when that is still
    /// empty, which is what stops the selector from matching the same kind of control somewhere
    /// else.
    /// </summary>
    private async void OnPickElement(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: StepParameterViewModel parameter }
            || DataContext is not AddActionViewModel viewModel)
        {
            return;
        }

        if (await ElementPickerWindow.PickAsync(this) is not { } picked)
        {
            return;
        }

        parameter.Text = picked.Selector;

        // The window filter is filled in for whichever field this action keeps it in, so a step
        // that is anchored to a control gets the same narrowing as one that looks the control up.
        foreach (var name in new[] { "window", "anchorWindow" })
        {
            if (viewModel.Parameters.FirstOrDefault(item => item.Definition.Name == name) is { } filter
                && string.IsNullOrWhiteSpace(filter.Text))
            {
                filter.Text = picked.Window;
            }
        }
    }

    /// <summary>
    /// Opens the page a browser step is about and writes the selector of the element clicked on
    /// it. The address comes from the step's own address field, so "go to this page, then click
    /// that" is picked off the page the macro will actually be looking at.
    /// </summary>
    private async void OnPickBrowserElement(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: StepParameterViewModel parameter }
            || DataContext is not AddActionViewModel viewModel)
        {
            return;
        }

        var url = viewModel.Parameters.FirstOrDefault(item => item.Definition.Name == "url")?.Text
            ?? string.Empty;

        try
        {
            if (await BrowserPicker.PickAsync(url) is { Length: > 0 } selector)
            {
                parameter.Text = selector;
            }
        }
        catch (Exception)
        {
            // The dialog can be dismissed while the page is open, and closing it takes the browser
            // down with it — there is nowhere left to show a message about that.
            if (!_closed)
            {
                await ConfirmDialog.ShowAsync(this, Strings.Get("Add.PickBrowserElement"),
                    Strings.Get("Add.BrowserPickFailed"), Strings.Get("Common.Ok"), showCancel: false);
            }
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

    /// <summary>Noted so a pick that outlives the dialog does not try to report back to it.</summary>
    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        base.OnClosed(e);
    }
}
