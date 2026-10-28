using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

/// <summary>Asks which field of a record belongs in a scalar parameter.</summary>
public sealed class VariableFieldPickerWindow : Window
{
    public VariableFieldPickerWindow(IReadOnlyList<VariableDragField> fields)
    {
        Title = Strings.Get("VariableDrop.ChooseField");
        Width = 380;
        Height = 360;
        MinWidth = 320;
        MinHeight = 260;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowDecorations = WindowDecorations.BorderOnly;
        Background = (IBrush?)Application.Current?.Resources["AppBackdrop"] ?? Brushes.Transparent;

        var list = new ListBox
        {
            ItemsSource = fields,
            SelectedIndex = fields.Count > 0 ? 0 : -1,
            Margin = new Thickness(18, 14, 18, 8),
        };
        list.ItemTemplate = new FuncDataTemplate<VariableDragField>((field, _) =>
            new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock
                    {
                        Text = field.Name,
                        FontSize = 13,
                        Foreground = (IBrush?)Application.Current?.Resources["AppText"] ?? Brushes.White,
                    },
                    new TextBlock
                    {
                        Text = Strings.Get($"Variable.Type.{ActionReturn.TypeNameOf(field.Type)}",
                            ActionReturn.TypeNameOf(field.Type)),
                        FontSize = 11.5,
                        Foreground = (IBrush?)Application.Current?.Resources["AppMuted"] ?? Brushes.Gray,
                    },
                },
            });

        var use = new Button
        {
            Content = Strings.Get("Common.Ok"),
            MinWidth = 88,
            IsDefault = true,
        };
        var cancel = new Button
        {
            Content = Strings.Get("Common.Cancel"),
            MinWidth = 88,
            IsCancel = true,
        };
        use.Click += (_, _) => Close(list.SelectedItem as VariableDragField);
        cancel.Click += (_, _) => Close(null);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(18, 8, 18, 16),
            Children = { cancel, use },
        };

        Content = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("Auto,*,Auto"),
            Children =
            {
                new TextBlock
                {
                    Text = Strings.Get("VariableDrop.ChooseFieldHint"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(18, 16, 18, 0),
                    Foreground = (IBrush?)Application.Current?.Resources["AppMuted"] ?? Brushes.Gray,
                },
                Place(list, 1),
                Place(buttons, 2),
            },
        };
    }

    private static T Place<T>(T control, int row)
        where T : Control
    {
        Grid.SetRow(control, row);
        return control;
    }
}
