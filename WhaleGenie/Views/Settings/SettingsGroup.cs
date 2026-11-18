using Avalonia;
using Avalonia.Controls;

namespace WhaleGenie.Views;

/// <summary>
/// 一页里的一组设置：标题、说明与全部设置行平铺在一起。分组没有展开动作，标题行也不是按钮，
/// 读的时候整组内容都在；行仍由 <see cref="FluentAvalonia.UI.Controls.FASettingsExpanderItem"/> 呈现。
/// </summary>
public sealed class SettingsGroup : ContentControl
{
    public static readonly StyledProperty<object?> HeaderProperty =
        AvaloniaProperty.Register<SettingsGroup, object?>(nameof(Header));

    public SettingsGroup()
    {
        Focusable = false;
    }

    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }
}
