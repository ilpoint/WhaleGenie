using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Core.Execution;
using WhaleGenie.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

public partial class AddActionWindow : Window
{
    /// <summary>Whether the dialog has been dismissed, which can happen while a page is open.</summary>
    private bool _closed;

    private IDeviceLayer? _devices;

    public AddActionWindow()
        : this(null, null, null, null, null, null)
    {
    }

    /// <summary>
    /// The devices the test button sends through — the real ones unless a check has put its own
    /// here. Made the first time the button is used, so opening the dialog costs nothing.
    /// </summary>
    internal IDeviceLayer Devices
    {
        get => _devices ??= new WindowsDeviceLayer();
        set => _devices = value;
    }

    /// <summary>
    /// Opens the dialog, optionally preloaded with a step being edited and optionally
    /// restricted to a catalogue subset, such as the condition actions.
    /// </summary>
    public AddActionWindow(MacroStep? existing, IReadOnlyList<ActionDefinition>? actions = null,
        IReadOnlyList<VariableChoice>? variables = null, IReadOnlyList<string>? macros = null,
        string? presetKey = null, string? assetFolder = null,
        IReadOnlyList<ActionParameterOption>? steps = null, string? newStepId = null)
    {
        InitializeComponent();

        var viewModel = new AddActionViewModel(actions, variables, macros, steps, newStepId);
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
        var steps = (DataContext as AddActionViewModel)?.StepChoices;
        var dialog = new AddActionWindow(null, list.Catalog, variables, null, null, assets, steps,
            FreeStepId(steps));
        var step = await dialog.ShowDialogOver<MacroStep?>(this);

        if (step is not null)
        {
            list.AddStep(step);
        }
    }

    /// <summary>
    /// A name for a step that is being added from inside another dialog. The editor settles the
    /// whole tree when the outer step joins the list, so a name drawn from the ones already in the
    /// macro is one it will keep — which is what the variables this step creates are named after.
    /// </summary>
    private static string FreeStepId(IReadOnlyList<ActionParameterOption>? steps)
        => StepIds.Next(new HashSet<string>(
            (steps ?? []).Select(option => option.Value), StringComparer.OrdinalIgnoreCase));

    /// <summary>Opens a picker for an existing nested step and swaps in the result.</summary>
    private async void OnNestedEditRequested(MacroStep step)
    {
        var variables = (DataContext as AddActionViewModel)?.CollectVariables();
        var assets = (DataContext as AddActionViewModel)?.AssetFolder;
        var steps = (DataContext as AddActionViewModel)?.StepChoices;
        var dialog = new AddActionWindow(step, null, variables, null, null, assets, steps);
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

    /// <summary>Adds an empty place to look at to a step's list of them.</summary>
    private void OnAddRegionRow(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: StepParameterViewModel parameter })
        {
            parameter.AddRegion();
        }
    }

    /// <summary>Adds another press to a key run.</summary>
    private void OnAddKeyRow(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: StepParameterViewModel parameter })
        {
            parameter.AddKeyRow();
        }
    }

    /// <summary>
    /// Opens the keyboard drawn on screen for one press of a key run, and joins the key whose cap
    /// was clicked to that press. The key already there stays, because a combination of several
    /// keys is built a key at a time.
    /// </summary>
    private async void OnPickKeyRow(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: KeyRowViewModel row })
        {
            return;
        }

        if (await VirtualKeyboardWindow.PickAsync(this) is { Length: > 0 } key)
        {
            row.AddKey(key);
        }
    }

    /// <summary>
    /// Drags a rectangle out on the screen and writes it into the row that asked for it, which is
    /// how a region is given without the four numbers being read off a ruler.
    /// </summary>
    private async void OnPickRegionRow(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: RegionRowViewModel row })
        {
            return;
        }

        if (await RegionPickerWindow.PickAsync(this) is { } picked)
        {
            row.Put(picked.X, picked.Y, picked.Width, picked.Height);
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

    /// <summary>
    /// Opens the variable picker on the field that asked for it. A field that can name a variable
    /// should never need the name typed out of memory, and the name goes in where the caret is: a
    /// field is often a piece of text with a variable beside it rather than only a variable.
    /// </summary>
    private async void OnPickVariable(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: StepParameterViewModel parameter } button
            || DataContext is not AddActionViewModel viewModel)
        {
            return;
        }

        if (await VariablePickerWindow.ShowFor(this, viewModel.CollectVariables())
            is not { Length: > 0 } picked)
        {
            return;
        }

        // The caret belongs to the box beside the button, which is the row's own field: the box
        // holds one value per row, so the one on this row is the one that was being filled in.
        var caret = button.FindAncestorOfType<Grid>(true)?.GetVisualDescendants()
            .OfType<AutoCompleteBox>().FirstOrDefault()?.CaretIndex;
        parameter.InsertVariable(picked, caret ?? (parameter.Text ?? string.Empty).Length);
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

    /// <summary>
    /// Opens the keyboard drawn on screen and keeps the key whose cap was clicked. A key is a name
    /// the macro has to spell exactly, so it is taken off a picture of a keyboard rather than out
    /// of a list of names.
    /// </summary>
    private async void OnOpenKeyPad(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: StepParameterViewModel parameter })
        {
            return;
        }

        if (await VirtualKeyboardWindow.PickAsync(this) is { Length: > 0 } key)
        {
            // A combination is put together a key at a time, so the key that was clicked joins
            // what is written; a field holding one key has nothing to join and is set outright.
            if (parameter.IsKeys)
            {
                parameter.AddKey(key);
            }
            else
            {
                parameter.Text = key;
            }
        }
    }

    /// <summary>
    /// Opens the controller drawn on screen and keeps the control that was clicked. Which question
    /// the pad answers — a button, a stick or a trigger — follows from the parameter it was opened
    /// from, because that is what the step is asking for.
    /// </summary>
    private async void OnOpenGamepadPad(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: StepParameterViewModel parameter })
        {
            return;
        }

        var pick = parameter.Definition.Name switch
        {
            "stick" => GamepadPick.Stick,
            "trigger" => GamepadPick.Trigger,
            _ => GamepadPick.Button,
        };

        if (await VirtualGamepadWindow.PickAsync(this, pick) is { Length: > 0 } value)
        {
            parameter.Choose(value);
        }
    }

    /// <summary>
    /// Tries the step for real, once, which is the same question the element picker's test asks:
    /// is this the thing I meant? A key name can be spelled another way and a controller control
    /// can be the wrong one of a pair, so the answer is to send it and let the user watch — in the
    /// window they meant it for, which is why the dialog steps aside first, the way picking does.
    /// </summary>
    private async void OnTestStep(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: StepParameterViewModel parameter }
            || DataContext is not AddActionViewModel viewModel)
        {
            return;
        }

        if (parameter.CurrentText.Trim().Length == 0)
        {
            await ReportTestAsync(Strings.Get("Add.TestFieldEmpty"));
            return;
        }

        // The step as it would be saved: what is tried is what was written, settings and all.
        var step = new[] { viewModel.BuildStep() }.ToExecutable()[0];

        // Out of the way while it is tried, so the key or the control lands on the window that was
        // meant, which is whatever the user had in mind behind this dialog.
        var previous = WindowState;
        WindowState = WindowState.Minimized;

        RunResult outcome;
        try
        {
            // Off the thread that draws the window: a device call can wait on hardware, and the
            // dialog has to stay able to come back when it is done.
            outcome = await Task.Run(() => MacroRunner.TryAsync(step, Devices));
        }
        finally
        {
            // The dialog can be dismissed while the trial runs, and a window on its way out has
            // nothing left to come back to.
            if (!_closed)
            {
                WindowState = previous == WindowState.Maximized
                    ? WindowState.Maximized
                    : WindowState.Normal;
                Activate();
            }
        }

        if (outcome.Status is not RunStatus.Completed && !_closed)
        {
            await ReportTestAsync(TrialFailure(outcome));
        }
    }

    /// <summary>Why a step could not be tried, in the interface's own words.</summary>
    private static string TrialFailure(RunResult outcome)
        => outcome.Detail.Length == 0
            ? Strings.Get(outcome.Key)
            : Strings.Format(outcome.Key, outcome.Detail);

    /// <summary>
    /// Looks at the screen the way this step would and shows what turned up, without doing anything
    /// about it. A step that clicks is the reason this exists: trying <c>vision.clickImage</c> for
    /// real clicks on whatever the user has on screen, so what is tried is the looking.
    /// </summary>
    private async void OnLookOnce(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AddActionViewModel viewModel)
        {
            return;
        }

        // The step as it would be saved, so what is looked at is what was written.
        var step = new[] { viewModel.BuildStep() }.ToExecutable()[0];

        // Off the thread that draws the window: reading the screen and matching a picture can take
        // a moment, and the dialog has to stay able to come back when it is done.
        var outcome = await Task.Run(() => MacroRunner.LookOnce(step, Devices));
        if (outcome.Look is null)
        {
            await ReportTestAsync(TrialFailure(
                new RunResult(RunStatus.Failed, outcome.Key, outcome.Detail, 0)));
            return;
        }

        LookWindow.Show(outcome.Look, this);
    }

    /// <summary>
    /// Picks where this step's click should land, on the picture it looks for: clicking the middle
    /// of a found picture is wrong whenever the thing to hit sits beside it, and working the offset
    /// out in the head is the kind of arithmetic a person gets wrong quietly.
    /// </summary>
    private async void OnPickOffset(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AddActionViewModel viewModel)
        {
            return;
        }

        var field = viewModel.Parameters
            .FirstOrDefault(parameter => parameter.Definition.Name == "image");

        // The picture may be held in a variable the run has not made yet, and there is nothing to
        // point at before one is chosen.
        var path = field is null ? null : ImageAssets.Resolve(field.CurrentText, field.AssetFolder);
        if (path is null)
        {
            await ReportTestAsync(Strings.Get("Add.PickOffsetNeedsImage"));
            return;
        }

        ImageFrame? picture;
        try
        {
            picture = await Task.Run(() => Devices.Vision.Load(path));
        }
        catch (Exception error)
        {
            await ReportTestAsync(error.Message);
            return;
        }

        if (picture is null || picture.IsEmpty)
        {
            await ReportTestAsync(Strings.Format("Add.PickOffsetNeedsImage"));
            return;
        }

        var offset = await OffsetPickerWindow.PickAsync(this, picture, Strings.Get("Offset.Title"));
        if (offset is { } point)
        {
            viewModel.ApplyOffset(point.X, point.Y);
        }
    }

    /// <summary>
    /// Puts the step's name on the clipboard. It is a name people write down — into a condition, a
    /// message, a note to whoever reads the macro next — and typing four characters off a screen is
    /// where they get it wrong.
    /// </summary>
    private async void OnCopyStepId(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AddActionViewModel { HasStepId: true } viewModel)
        {
            return;
        }

        if (Clipboard is { } clipboard)
        {
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.CreateText(viewModel.StepId));
            await clipboard.SetDataAsync(transfer);
        }
    }

    /// <summary>
    /// Picks the file or folder a path field is about, with the dialog that suits which way the
    /// path is going: reading, writing, or a folder. What comes back is stored the way the engine
    /// will read it — relative when it is inside the macros folder, in full otherwise.
    /// </summary>
    private async void OnBrowsePath(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: StepParameterViewModel parameter })
        {
            return;
        }

        var filters = Filter(parameter.Definition.PathFilter);
        var picked = parameter.Definition.PathIntent switch
        {
            PathIntent.Write => await PickToWrite(parameter, filters),
            PathIntent.Folder => await PickFolder(),
            _ => await PickToRead(parameter, filters),
        };

        if (picked is { Length: > 0 })
        {
            parameter.Text = MacroPaths.ForMacro(picked);
        }
    }

    /// <summary>The kinds of file a field is about, as dialog filters, or none for any file.</summary>
    private static IReadOnlyList<FilePickerFileType> Filter(string patterns)
    {
        if (patterns.Length == 0)
        {
            return [];
        }

        var wanted = patterns.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return
        [
            new FilePickerFileType(string.Join(" / ", wanted)) { Patterns = wanted },
            new FilePickerFileType(Strings.Get("Add.AllFiles")) { Patterns = ["*.*"] },
        ];
    }

    private async Task<string> PickToRead(StepParameterViewModel parameter,
        IReadOnlyList<FilePickerFileType> filters)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.Format("Add.BrowseOpenTitle", parameter.Definition.LocalLabel),
            AllowMultiple = false,
            FileTypeFilter = filters,
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() ?? string.Empty : string.Empty;
    }

    private async Task<string> PickToWrite(StepParameterViewModel parameter,
        IReadOnlyList<FilePickerFileType> filters)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.Format("Add.BrowseSaveTitle", parameter.Definition.LocalLabel),
            SuggestedFileName = parameter.Text.Trim(),
            FileTypeChoices = filters,
        });

        return file?.TryGetLocalPath() ?? string.Empty;
    }

    private async Task<string> PickFolder()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Strings.Get("Add.BrowseFolderTitle"),
            AllowMultiple = false,
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() ?? string.Empty : string.Empty;
    }

    /// <summary>
    /// Gives a result variable a name nothing else is using, by putting a number after the one it
    /// has. It is what the line under such a field offers, so noticing a clash and doing something
    /// about it is one click rather than a rewrite of the name.
    /// </summary>
    private void OnMakeNameUnique(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: StepParameterViewModel parameter })
        {
            parameter.MakeNameUnique();
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

    /// <summary>
    /// Flashes the control the selector names, so a pick can be checked without running the step.
    /// The dialog steps aside first, the same way picking does: the control being looked at is
    /// usually behind the window asking about it.
    /// </summary>
    private async void OnTestElement(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: StepParameterViewModel parameter }
            || DataContext is not AddActionViewModel viewModel)
        {
            return;
        }

        if (Selector(parameter) is not { Length: > 0 } selector)
        {
            await ReportTestAsync(Strings.Get("Add.TestElementEmpty"));
            return;
        }

        // A control is measured from the same window field the picker fills in, so which one that
        // is follows the field being tested rather than the action.
        var filter = ParameterText(viewModel,
            parameter.Definition.Name == "anchorSelector" ? "anchorWindow" : "window");

        // Out of the way while the frame is up, the same way picking does it: the control is
        // usually behind the very window asking about it. The dialog waits the frame out, so it
        // never comes back on top of the thing it just said to look at.
        var previous = WindowState;
        WindowState = WindowState.Minimized;

        ElementFlashWindow? flash = null;
        try
        {
            if (ElementFlashWindow.Locate(viewModel.Ui, selector, filter) is { } found)
            {
                flash = ElementFlashWindow.Show(found);
                if (flash is not null)
                {
                    await flash.Finished;
                }
            }
        }
        finally
        {
            // The dialog can be dismissed while the frame is up, and a window on its way out has
            // nothing left to come back to.
            if (!_closed)
            {
                WindowState = previous == WindowState.Maximized
                    ? WindowState.Maximized
                    : WindowState.Normal;
                Activate();
            }
        }

        if (flash is null && !_closed)
        {
            await ReportTestAsync(Strings.Get("Add.TestElementMissing"));
        }
    }

    /// <summary>
    /// Outlines the element a page selector names on the page it belongs to, which is the same
    /// question the desktop picker answers next to it.
    /// </summary>
    private async void OnTestBrowserElement(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: StepParameterViewModel parameter })
        {
            return;
        }

        if (Selector(parameter) is not { Length: > 0 } selector)
        {
            await ReportTestAsync(Strings.Get("Add.TestElementEmpty"));
            return;
        }

        var found = false;
        try
        {
            found = await BrowserPicker.HighlightAsync(selector);
        }
        catch (Exception)
        {
            // The dialog can be dismissed while the page is being asked, and a window that is on
            // its way out is not worth reporting to.
        }

        if (!found && !_closed)
        {
            await ReportTestAsync(Strings.Get("Add.TestElementMissing"));
        }
    }

    /// <summary>The selector a field holds, or null when nothing has been picked or written yet.</summary>
    private static string? Selector(StepParameterViewModel parameter)
    {
        var text = (parameter.Text ?? string.Empty).Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>The text of a parameter of this action, or an empty string when it has none.</summary>
    private static string ParameterText(AddActionViewModel viewModel, string name)
        => viewModel.Parameters.FirstOrDefault(item => item.Definition.Name == name)?.Text ?? string.Empty;

    /// <summary>Says what the test found, without leaving the dialog.</summary>
    private async Task ReportTestAsync(string message)
        => await ConfirmDialog.ShowAsync(this, Strings.Get("Add.TestElement"), message,
            Strings.Get("Common.Ok"), showCancel: false);

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

        // The devices a trial used go with the dialog, so a controller it put on the machine does
        // not outlive the window that asked for it.
        (_devices as IDisposable)?.Dispose();
        _devices = null;

        base.OnClosed(e);
    }
}
