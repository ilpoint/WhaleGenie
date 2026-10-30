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

        // 窗自己的底：这是"窗体"属性，不是某一类控件的外观，所以拿令牌而不是类（规则 7）。
        Background = Brush("SurfaceWindow");

        var list = new ListBox
        {
            ItemsSource = fields,
            SelectedIndex = fields.Count > 0 ? 0 : -1,
            Classes = { "List" },
        };

        // 一行 = 名字 + 类型。两个类各自说清"这一行里这是什么"：
        // `Body` 是主体文字（13）、`Caption` 是说明小字（11）——**层级由类表达，不由字号字面表达**。
        list.ItemTemplate = new FuncDataTemplate<VariableDragField>((field, _) =>
            new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock { Text = field.Name, Classes = { "Body" } },
                    new TextBlock
                    {
                        Text = Strings.Get($"Variable.Type.{ActionReturn.TypeNameOf(field.Type)}",
                            ActionReturn.TypeNameOf(field.Type)),
                        Classes = { "Caption" },
                    },
                },
            });

        var use = new Button
        {
            Content = Strings.Get("Common.Ok"),
            MinWidth = 88,
            IsDefault = true,
            Classes = { "Primary" },
        };
        var cancel = new Button
        {
            Content = Strings.Get("Common.Cancel"),
            MinWidth = 88,
            IsCancel = true,
            Classes = { "Secondary" },
        };
        use.Click += (_, _) => Close(list.SelectedItem as VariableDragField);
        cancel.Click += (_, _) => Close(null);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancel, use },
        };

        // 整页的内缩只有这一处：页边距是"这一页怎么摆"，交给一个容器，而不是每个孩子各写一遍
        // `Margin`——那是上一轮在窗口里删掉的那种东西，C# 里同样不该写。
        Content = new Border
        {
            Padding = new Thickness(16, 14, 16, 14),
            Child = new Grid
            {
                RowDefinitions = RowDefinitions.Parse("Auto,*,Auto"),
                Children =
                {
                    new TextBlock
                    {
                        Text = Strings.Get("VariableDrop.ChooseFieldHint"),
                        Classes = { "Hint" },
                    },
                    Place(list, 1),
                    Place(buttons, 2),
                },
            },
        };
    }

    private static IBrush? Brush(string key)
        => Application.Current?.Resources.TryGetResource(key, Application.Current.ActualThemeVariant, out var value) == true
            ? value as IBrush
            : null;

    private static T Place<T>(T control, int row)
        where T : Control
    {
        Grid.SetRow(control, row);
        return control;
    }
}
