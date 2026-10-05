using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Viktor.Models;
using Viktor.ViewModels;

namespace Viktor.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var minimizeButton = this.FindControl<Button>("MinimizeButton");
        if (minimizeButton is not null)
        {
            minimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;
        }

        var closeButton = this.FindControl<Button>("CloseButton");
        if (closeButton is not null)
        {
            closeButton.Click += (_, _) => Close();
        }

        var addMacroButton = this.FindControl<Button>("AddMacroButton");
        if (addMacroButton is not null)
        {
            addMacroButton.Click += async (_, _) => await ShowMacroEditorAsync();
        }
    }

    private async Task ShowMacroEditorAsync()
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var editor = new MacroEditorWindow();
        var macro = await editor.ShowDialog<MacroItem?>(this);

        if (macro is not null)
        {
            viewModel.AddMacro(macro);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            && e.GetPosition(this).Y <= 36)
        {
            BeginMoveDrag(e);
        }
    }
}