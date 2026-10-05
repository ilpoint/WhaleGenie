using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Viktor.Models;

namespace Viktor.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SystemLabel))]
    public partial bool IsRunning { get; set; }

    /// <summary>System toggle label while the macro is running.</summary>
    public string SystemLabel => IsRunning ? "Enable System" : "Disable System";

    public string Version { get; } = "v0.0.1";

    public ObservableCollection<MacroItem> Macros { get; } =
    [
        new MacroItem
        {
            Name = "Macro 1",
            Trigger = "As long as Enter Button",
            Action = "pressed and hold"
        }
    ];

    /// <summary>Adds a macro created in the macro editor and assigns it a display name.</summary>
    public void AddMacro(MacroItem macro)
    {
        if (string.IsNullOrWhiteSpace(macro.Name))
        {
            macro.Name = $"Macro {Macros.Count + 1}";
        }

        Macros.Add(macro);
    }

    [RelayCommand]
    private void ToggleRun() => IsRunning = !IsRunning;
}
