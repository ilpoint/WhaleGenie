using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace WhaleGenie.Views;

/// <summary>
/// 一个顶层窗口里唯一的说明气泡：状态机、350/200 毫秒调度、指针是否仍落在图标或气泡上的判定，
/// 以及 Esc、点击外部、换页、关窗时的统一收起。说明控件只登记自己并把指针与点击转进来。
/// </summary>
/// <remarks>
/// 指针不再靠 <c>PointerEntered</c>/<c>PointerExited</c> 成对到达来维持：浮层弹出后事件由另一个
/// 顶层接收，成对性没有保证。宿主以顶层坐标比较"图标矩形 ∪ 气泡矩形"，两个事件只当作指针可能
/// 变了的唤醒；气泡自己仍然是独立顶层，指针在它上面时由它自己的进出事件保持打开。
/// </remarks>
internal sealed class HintBubbleHost
{
    private static readonly ConditionalWeakTable<TopLevel, HintBubbleHost> Hosts = [];

    private static readonly TimeSpan HoverDelay = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan CloseDelay = TimeSpan.FromMilliseconds(200);

    private const double PanelMaxWidth = 360;
    private const double PanelMaxHeight = 320;

    private readonly TopLevel _top;
    private readonly SelectableTextBlock _body;
    private readonly Border _panel;
    private readonly Popup _popup;
    private Rect _panelRect;

    private HintBubbleHost(TopLevel top)
    {
        _top = top;

        _body = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap };
        _body.Bind(TextBlock.FontSizeProperty, top.GetResourceObservable("UiBodySmallSize"));
        _body.Bind(TextBlock.ForegroundProperty, top.GetResourceObservable("TextPrimary"));
        _body.Bind(TextBlock.LineHeightProperty, top.GetResourceObservable("UiBodySmallLine"));

        var scroll = new ScrollViewer
        {
            Content = _body,
            MaxHeight = PanelMaxHeight,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        _panel = new Border
        {
            Width = PanelMaxWidth,
            MaxWidth = PanelMaxWidth,
            BorderThickness = new Thickness(1),
            Child = scroll,
        };
        _panel.Bind(Border.PaddingProperty, top.GetResourceObservable("PadSection"));
        _panel.Bind(Border.CornerRadiusProperty, top.GetResourceObservable("RadiusContainer"));
        _panel.Bind(Border.BackgroundProperty, top.GetResourceObservable("SurfaceCard"));
        _panel.Bind(Border.BorderBrushProperty, top.GetResourceObservable("BorderControl"));
        _panel.PointerEntered += (_, _) => OnPanelEntered();
        _panel.PointerExited += (_, _) => OnPanelLeft();
        _panel.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);

        _popup = new Popup
        {
            Placement = PlacementMode.Bottom,
            PlacementConstraintAdjustment = PopupPositionerConstraintAdjustment.None,
            Child = _panel,
            IsLightDismissEnabled = false,
        };

        top.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
        top.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        top.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);

        // 换页签只是把页面面板切到不可见，触发行自己的 IsVisible 不会变；每次布局后复核一次
        // 有效可见性，气泡就不会留在已经翻过去的那一页上。
        top.LayoutUpdated += (_, _) =>
        {
            if (CurrentOwner is { } owner && !owner.IsEffectivelyVisible)
            {
                Dismiss(HintBubbleCloseReason.OwnerHidden);
            }
        };

        if (top is Window window)
        {
            window.PropertyChanged += (_, e) =>
            {
                if (e.Property == Window.IsVisibleProperty && !window.IsVisible)
                {
                    Dismiss(HintBubbleCloseReason.WindowHidden);
                }
            };
            window.Closed += (_, _) => Dismiss(HintBubbleCloseReason.WindowHidden);
        }
    }

    internal IHintDelayScheduler Delays { get; set; } = new DispatcherHintDelay();

    internal HintInfo? CurrentOwner { get; private set; }

    internal HintBubbleState State { get; private set; }

    internal int OpenCount { get; private set; }

    internal HintBubbleCloseReason LastCloseReason { get; private set; }

    internal Rect PanelRect => _panelRect;

    /// <summary>The text block inside the panel; the visual checks read its colours and metrics.</summary>
    internal SelectableTextBlock Body => _body;

    internal Border Panel => _panel;

    internal bool IsPanelOpen => _popup.IsOpen;

    internal static HintBubbleHost For(TopLevel top)
        => Hosts.GetValue(top, key => new HintBubbleHost(key));

    internal void NotifyPointerOver(HintInfo owner, Point at)
    {
        if (!owner.IsEffectivelyVisible || string.IsNullOrWhiteSpace(owner.Text))
        {
            return;
        }

        if (CurrentOwner == owner)
        {
            if (State == HintBubbleState.Pinned)
            {
                return;
            }

            if (State == HintBubbleState.Closing)
            {
                Delays.CancelAll();
                State = HintBubbleState.Open;
                return;
            }

            if (State == HintBubbleState.Closed)
            {
                BeginHover(owner);
            }

            return;
        }

        if (State is HintBubbleState.Open or HintBubbleState.Closing or HintBubbleState.Pinned)
        {
            Dismiss(HintBubbleCloseReason.AnotherOpened);
        }

        BeginHover(owner);
    }

    internal void NotifyPointerLeft(HintInfo owner, Point at)
    {
        if (CurrentOwner != owner)
        {
            return;
        }

        NotifyPointerMoved(at);
    }

    internal void NotifyPointerMoved(Point at)
    {
        if (CurrentOwner is not { } owner || State is HintBubbleState.Closed or HintBubbleState.Pinned)
        {
            return;
        }

        if (IsInside(owner, at))
        {
            if (State == HintBubbleState.Closing)
            {
                Delays.CancelAll();
                State = HintBubbleState.Open;
            }

            return;
        }

        if (State == HintBubbleState.HoverPending)
        {
            Delays.CancelAll();
            CurrentOwner = null;
            State = HintBubbleState.Closed;
            LastCloseReason = HintBubbleCloseReason.PointerLeft;
            return;
        }

        if (State == HintBubbleState.Open)
        {
            State = HintBubbleState.Closing;
            Delays.Schedule(CloseDelay, () =>
            {
                if (State == HintBubbleState.Closing)
                {
                    Dismiss(HintBubbleCloseReason.PointerLeft);
                }
            });
        }
    }

    internal void TogglePin(HintInfo owner)
    {
        if (!owner.IsEffectivelyVisible)
        {
            return;
        }

        if (CurrentOwner == owner)
        {
            if (State == HintBubbleState.Pinned)
            {
                Dismiss(HintBubbleCloseReason.Toggled);
                return;
            }

            if (State is HintBubbleState.Open or HintBubbleState.Closing)
            {
                Delays.CancelAll();
                State = HintBubbleState.Pinned;
                return;
            }
        }

        if (State is HintBubbleState.Open or HintBubbleState.Closing or HintBubbleState.Pinned)
        {
            Dismiss(HintBubbleCloseReason.AnotherOpened);
        }

        Delays.CancelAll();
        CurrentOwner = owner;
        Show(owner);
        State = HintBubbleState.Pinned;
        OpenCount++;
    }

    internal void NotifyTextChanged(HintInfo owner)
    {
        if (CurrentOwner != owner)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(owner.Text))
        {
            Dismiss(HintBubbleCloseReason.TextCleared);
            return;
        }

        _body.Text = owner.Text;
    }

    internal void NotifyOwnerHidden(HintInfo owner)
    {
        if (CurrentOwner == owner)
        {
            Dismiss(HintBubbleCloseReason.OwnerHidden);
        }
    }

    internal void NotifyDetached(HintInfo owner)
    {
        if (CurrentOwner == owner)
        {
            Dismiss(HintBubbleCloseReason.Detached);
        }
    }

    internal bool DismissIfOpen(HintBubbleCloseReason reason)
    {
        if (State == HintBubbleState.Closed)
        {
            return false;
        }

        Dismiss(reason);
        return true;
    }

    internal void Dismiss(HintBubbleCloseReason reason)
    {
        Delays.CancelAll();
        if (_popup.IsOpen)
        {
            _popup.IsOpen = false;
        }

        CurrentOwner = null;
        State = HintBubbleState.Closed;
        _panelRect = default;
        LastCloseReason = reason;
    }

    private void BeginHover(HintInfo owner)
    {
        Delays.CancelAll();
        CurrentOwner = owner;
        State = HintBubbleState.HoverPending;
        Delays.Schedule(HoverDelay, OpenPending);
    }

    private void OpenPending()
    {
        if (CurrentOwner is not { } owner || State != HintBubbleState.HoverPending)
        {
            return;
        }

        if (!owner.IsEffectivelyVisible || string.IsNullOrWhiteSpace(owner.Text))
        {
            Dismiss(HintBubbleCloseReason.OwnerHidden);
            return;
        }

        Show(owner);
        State = HintBubbleState.Open;
        OpenCount++;
    }

    private void Show(HintInfo owner)
    {
        _body.Text = owner.Text;

        // Popup 只有接进某一层的可视树才会真的开出一个顶层；挂到所属窗口的 OverlayLayer，
        // 不因此改变窗口内容布局。换到另一个窗口的图标时把旧挂点摘掉。
        if (OverlayLayer.GetOverlayLayer(owner) is { } overlay)
        {
            if (_popup.Parent is Panel previous && !ReferenceEquals(previous, overlay))
            {
                previous.Children.Remove(_popup);
            }

            if (!overlay.Children.Contains(_popup))
            {
                overlay.Children.Add(_popup);
            }
        }

        var available = new Rect(_top.ClientSize);
        if (available.Width <= 0 || available.Height <= 0)
        {
            available = new Rect(_top.Bounds.Size);
        }

        _panel.Measure(new Size(PanelMaxWidth, PanelMaxHeight));
        var desired = _panel.DesiredSize;
        if (desired.Width <= 0 || desired.Height <= 0)
        {
            desired = new Size(PanelMaxWidth, PanelMaxHeight);
        }

        var anchor = IconRect(owner);
        _panelRect = HintBubblePlacement.Place(anchor, available, desired);

        _popup.PlacementTarget = owner;
        _popup.HorizontalOffset = _panelRect.X - anchor.X;
        _popup.VerticalOffset = _panelRect.Y - anchor.Bottom;
        _popup.IsOpen = true;
    }

    private void OnPanelEntered()
    {
        if (State == HintBubbleState.Closing)
        {
            Delays.CancelAll();
            State = HintBubbleState.Open;
        }
    }

    private void OnPanelLeft()
    {
        if (State == HintBubbleState.Open)
        {
            State = HintBubbleState.Closing;
            Delays.Schedule(CloseDelay, () =>
            {
                if (State == HintBubbleState.Closing)
                {
                    Dismiss(HintBubbleCloseReason.PointerLeft);
                }
            });
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (CurrentOwner is null || State == HintBubbleState.Pinned)
        {
            return;
        }

        NotifyPointerMoved(e.GetPosition(_top));
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (CurrentOwner is not { } owner || State == HintBubbleState.Closed)
        {
            return;
        }

        if (!IsInside(owner, e.GetPosition(_top)))
        {
            Dismiss(HintBubbleCloseReason.OutsidePress);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !e.Handled && DismissIfOpen(HintBubbleCloseReason.Escape))
        {
            e.Handled = true;
        }
    }

    private bool IsInside(HintInfo owner, Point at)
        => IconRect(owner).Contains(at)
            || (State is HintBubbleState.Open or HintBubbleState.Closing or HintBubbleState.Pinned
                && _panelRect.Contains(at));

    private Rect IconRect(HintInfo owner)
        => owner.TranslatePoint(default, _top) is { } origin
            ? new Rect(origin, owner.Bounds.Size)
            : default;
}
