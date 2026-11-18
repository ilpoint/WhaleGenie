using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Lucide.Avalonia;
using WhaleGenie.Localization;

namespace WhaleGenie.Views;

/// <summary>
/// 标题旁的一枚信息图标：说明默认不占版面，点开才看。悬停只说"点开看说明"，点开是一块可换行的
/// 面板——长段落留在那里，而不是每一页都把整段话铺在字段底下。
/// </summary>
/// <remarks>
/// 面板用主题的卡片底色与边框，不自己上色：`FlyoutPresenter` 在这些窗口上默认是一块深色，
/// 看起来像一块黑，和表单的层次对不上。点开既看得完，又能选中复制。
/// </remarks>
public sealed class HintInfo : Button
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<HintInfo, string?>(nameof(Text));

    private readonly TextBlock _body = new()
    {
        TextWrapping = TextWrapping.Wrap,
        FontSize = 12.5,
    };

    public HintInfo()
    {
        Focusable = true;
        Padding = new Thickness(2, 0);
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        VerticalAlignment = VerticalAlignment.Center;
        Cursor = new Cursor(StandardCursorType.Hand);

        var icon = new IconView
        {
            Data = LucideIconKind.Info,
            Width = 14,
            Height = 14,
            StrokeThickness = 1.6,
        };
        icon.Bind(IconView.StrokeProperty, this.GetResourceObservable("TextTertiary"));
        Content = icon;

        // 卡片底色而不是窗口底色：窗口色与表单重合，一片深色看上去就是一块黑；卡片色比它亮一档，
        // 再加边框与阴影，才有"浮在上面的一张纸"的样子。
        var panel = new Border
        {
            MaxWidth = 360,
            Padding = new Thickness(12, 10),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BoxShadow = new BoxShadows(new BoxShadow
            {
                Blur = 24,
                OffsetY = 8,
                Color = Color.Parse("#66000000"),
            }),
            Child = _body,
        };
        panel.Bind(Border.BackgroundProperty, this.GetResourceObservable("SurfaceCard"));
        panel.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BorderControl"));

        Flyout = new Flyout
        {
            Placement = PlacementMode.Bottom,
            Content = panel,
        };

        AutomationProperties.SetName(this, Strings.Get("Add.ShowHint"));
        ToolTip.SetTip(this, Strings.Get("Add.ShowHint"));
        ToolTip.SetShowDelay(this, 400);

        Click += (_, _) => Flyout?.ShowAt(this);

        IsVisible = false;
    }

    /// <summary>What the panel says. Empty hides the icon rather than showing an empty panel.</summary>
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != TextProperty)
        {
            return;
        }

        var text = Text ?? string.Empty;
        _body.Text = text;
        IsVisible = text.Trim().Length > 0;
    }
}
