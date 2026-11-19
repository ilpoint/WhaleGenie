using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Lucide.Avalonia;
using WhaleGenie.Localization;

namespace WhaleGenie.Views;

/// <summary>
/// 标题旁的一枚信息图标：说明默认不占版面，指针停上去才看。悬停与开合由所在窗口的
/// <see cref="HintBubbleHost"/> 统一负责；这个控件只呈现图标、处理空文案，并把指针与点击转过去。
/// </summary>
/// <remarks>
/// 同一窗口里的气泡只有一个宿主，跨图标也不会同屏冒出两块；计时与关闭宽限不再留在这个控件里，
/// 换页或收起时它只通知宿主，不自己维护一条与浮层各说各话的状态。
/// </remarks>
public sealed class HintInfo : Button
{
    private const double RowHeight = 24;

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<HintInfo, string?>(nameof(Text));

    private HintBubbleHost? _host;
    private TopLevel? _top;

    public HintInfo()
    {
        Focusable = true;
        Width = RowHeight;
        Height = RowHeight;
        MinWidth = RowHeight;
        MinHeight = RowHeight;
        this.Bind(PaddingProperty, this.GetResourceObservable("Pad0"));
        HorizontalContentAlignment = HorizontalAlignment.Left;
        VerticalContentAlignment = VerticalAlignment.Center;
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
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        icon.Bind(IconView.StrokeProperty, this.GetResourceObservable("TextTertiary"));
        Content = icon;

        AutomationProperties.SetName(this, Strings.Get("Add.ShowHint"));
        AutomationProperties.SetHelpText(this, Strings.Get("Add.ShowHint"));

        PointerEntered += (_, e) =>
        {
            if (_host is not null && _top is not null)
            {
                _host.NotifyPointerOver(this, e.GetPosition(_top));
            }
        };
        PointerExited += (_, e) =>
        {
            if (_host is not null && _top is not null)
            {
                _host.NotifyPointerLeft(this, e.GetPosition(_top));
            }
        };
        Click += (_, _) => _host?.TogglePin(this);

        IsVisible = false;
    }

    /// <summary>What the panel says. Empty hides the icon rather than showing an empty panel.</summary>
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Whether the bubble is on screen. Read by the headless checks.</summary>
    internal bool IsBubbleOpen
        => State is HintBubbleState.Open or HintBubbleState.Pinned;

    /// <summary>Whether the bubble was pinned by a click, so leaving the icon must not close it.</summary>
    internal bool IsBubblePinned => State == HintBubbleState.Pinned;

    internal HintBubbleState State
        => _host is { } host && host.CurrentOwner == this
            ? host.State
            : HintBubbleState.Closed;

    internal int OpenCount => _host?.OpenCount ?? 0;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _top = TopLevel.GetTopLevel(this);
        _host = _top is null ? null : HintBubbleHost.For(_top);
        NormalizeInlineRow();
    }

    /// <summary>
    /// 把图标所在那一行的直接兄弟收成同一条中线：文字与勾选框居中，行内标签的 `GapField`
    /// 下边距只在这里清零（本地值只作用于这一行，普通字段继续取样式）。规则集中在这一个控件，
    /// 二十五处使用点因此不用各写一份裸 Margin 或新增样式类。
    /// </summary>
    private void NormalizeInlineRow()
    {
        if (Parent is not Panel row)
        {
            return;
        }

        foreach (var child in row.Children)
        {
            if (ReferenceEquals(child, this))
            {
                continue;
            }

            switch (child)
            {
                case TextBlock text:
                    Center(text);
                    var margin = text.Margin;
                    if (margin.Bottom != 0)
                    {
                        text.Margin = new Thickness(margin.Left, margin.Top, margin.Right, 0);
                    }

                    break;

                case CheckBox check:
                    Center(check);
                    // Fluent 的勾选框模板自带上下内边距与 32 的最小高度，光压 MinHeight、收敛 Padding 仍会
                    // 撑到 32；行内组合统一到 24 的结构高度，图标 20 不裁字，也不影响其它勾选框。
                    if (check.MinHeight < RowHeight)
                    {
                        check.MinHeight = RowHeight;
                    }

                    if (check.Padding != default)
                    {
                        check.Padding = default;
                    }

                    if (check.Height != RowHeight)
                    {
                        check.Height = RowHeight;
                    }

                    break;

                case Panel nested when nested.Children.Count > 0
                        && nested.Children.All(one => one is TextBlock):
                    Center(nested);
                    break;
            }
        }
    }

    private static void Center(Control control)
    {
        if (control.VerticalAlignment != VerticalAlignment.Center)
        {
            control.VerticalAlignment = VerticalAlignment.Center;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _host?.NotifyDetached(this);
        _host = null;
        _top = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == Visual.VisualParentProperty)
        {
            NormalizeInlineRow();
            return;
        }

        if (change.Property != TextProperty)
        {
            return;
        }

        var text = Text ?? string.Empty;
        IsVisible = text.Trim().Length > 0;
        _host?.NotifyTextChanged(this);
    }
}
