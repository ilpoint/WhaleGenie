using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;


namespace WhaleGenie.Views;

// Making the window: what it starts on, and the call that asks a person for one value.
public partial class ValuePickerWindow : AppWindow
{
    public ValuePickerWindow()
        : this(new ValuePickerViewModel(null, [], ActionReadMode.WholeValue))
    {
    }

    public ValuePickerWindow(ValuePickerViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        // The bar is where a window is dragged by; without this it is a stripe that does nothing.
        WindowChrome.Attach(this);
        Title = viewModel.Title;


        // A cell that only holds a name does not need a workbench: the window is the list.
        if (viewModel.NamesOnly)
        {
            Width = 560;
            Height = 620;
            MinWidth = 480;
            MinHeight = 520;
        }

        viewModel.CloseRequested += result => Close(result);

        // Undo has to be keyed in front of everything else: a text box keeps its own history and
        // would swallow the keys before a bubbling handler ever sees them.
        AddHandler(KeyDownEvent, OnWindowTunnelKey, RoutingStrategies.Tunnel);

        // The band is not a text box, so a letter typed while it has the focus has nowhere to go
        // unless the window forwards it to the small field.
        AddHandler(TextInputEvent, OnBandTextInput, RoutingStrategies.Bubble);

        // Every edit hands the band a new tree, so the piece the pointer was over no longer exists,
        // and the standing line has to be measured against the new tree as well.
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ValuePickerViewModel.Recipe))
            {
                Dispatcher.UIThread.Post(RefreshHover, DispatcherPriority.Loaded);
                Dispatcher.UIThread.Post(ShowCaret, DispatcherPriority.Loaded);
            }
            else if (args.PropertyName == nameof(ValuePickerViewModel.Caret))
            {
                Dispatcher.UIThread.Post(ShowCaret, DispatcherPriority.Loaded);
            }
        };

        BandScroll.SizeChanged += (_, _) =>
        {
            _viewModel.RefreshStacking(BandScroll.Viewport.Width);
            Dispatcher.UIThread.Post(ShowCaret, DispatcherPriority.Loaded);
        };

        Opened += (_, _) =>
        {
            _viewModel.RefreshStacking(BandScroll.Viewport.Width);
            if (StartCaret >= 0)
            {
                _viewModel.SetCaret(StartCaret);
            }

            Dispatcher.UIThread.Post(ShowCaret, DispatcherPriority.Loaded);
            if (!viewModel.CanCompute)
            {
                NameSearch.Focus();
                return;
            }

            // A cell that had a caret in it opens with the small field ready to type into; one that
            // did not keeps the variable search, which is where a formula usually starts.
            (StartCaret >= 0 ? ValueInput : VariableSearch).Focus();
        };
    }

    /// <summary>True when the window was opened from the formula side of a cell.</summary>
    public bool StartOnFormula { get; init; }

    /// <summary>Where the caret was in the cell, or -1 when nobody said.</summary>
    public int StartCaret { get; init; } = -1;

    /// <summary>
    /// Opens the window on a cell. A cell that only holds a name passes
    /// <see cref="ActionReadMode.VariableName"/>: a formula there is written down as the name
    /// itself, so offering one would be a promise the step cannot keep. Every other cell says how
    /// it is read, and the window checks the line the same way the field will read it.
    /// </summary>
    public static async Task<ValuePick> ShowFor(Window owner, string? seed,
        IReadOnlyList<VariableChoice>? variables,
        ActionReadMode readMode = ActionReadMode.WholeValue, bool bareNames = false,
        bool startOnFormula = false, string? target = null, int startCaret = -1,
        Action<MacroStep>? lookAtStep = null)
    {
        var viewModel = new ValuePickerViewModel(seed, variables, readMode, bareNames, target);
        if (lookAtStep is not null)
        {
            viewModel.Jumped += lookAtStep;
        }

        var window = new ValuePickerWindow(viewModel)
        {
            StartOnFormula = startOnFormula,
            StartCaret = startCaret,
        };

        return await window.ShowDialog<ValuePick>(owner) ?? ValuePick.Cancelled;
    }

    /// <summary>
    /// Writes one piece at the insertion point. The window stays open either way: a formula is built
    /// a piece at a time, and closing on the first piece would make composing one a series of openings.
    /// </summary>
    private void Put(string token) => _viewModel.Place(token);
}
