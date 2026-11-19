using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Converters;

/// <summary>
/// 运行状态文字的颜色：**状态落到状态色上**。
///
/// 原来是固定琥珀——那意味着"已完成"和"出问题了"长得一样，用户读到第三次就会开始忽略这个颜色。
/// 这不是配色问题：**状态没有被表达**。所以由状态种类决定颜色，而不是由一处写死的值决定。
/// </summary>
public class RunStatusBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            RunStatusKind.Done => "Success",
            RunStatusKind.Failed => "Danger",
            RunStatusKind.Paused => "Warning",
            RunStatusKind.Running => "TextPrimary",
            _ => "TextTertiary",
        };

        return Application.Current?.TryFindResource(key, out var found) == true ? found as IBrush : null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
