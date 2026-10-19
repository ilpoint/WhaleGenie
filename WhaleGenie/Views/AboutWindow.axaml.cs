using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using WhaleGenie.Execution;
using WhaleGenie.Localization;

namespace WhaleGenie.Views;

/// <summary>
/// Says what the program is, names the work it stands on, and shows where somebody who finds it
/// useful can leave a tip. The confirmation dialog could not carry the two codes, so this window
/// owns both the text and the two buttons that open longer documents.
/// </summary>
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        Title = Strings.Get("Main.About");
        this.FindControl<TextBlock>("AboutText")!.Text =
            Strings.Format("Main.AboutText", AppVersion.Text);

        var close = this.FindControl<Button>("CloseButton");
        if (close is not null)
        {
            close.Click += (_, _) => Close();
        }
    }

    private void OnOpenProjectClicked(object? sender, RoutedEventArgs e)
        => WebPage.Open(ProjectLinks.Home);

    private async void OnNoticesClicked(object? sender, RoutedEventArgs e)
        => await ConfirmDialog.ShowAsync(this, Strings.Get("Main.AboutNotices"),
            ThirdPartyNotices.Read(), Strings.Get("Common.Ok"), showCancel: false, height: 460);

    private void OnDoneClicked(object? sender, RoutedEventArgs e) => Close();

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
