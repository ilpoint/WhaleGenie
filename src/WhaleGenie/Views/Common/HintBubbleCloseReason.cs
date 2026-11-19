namespace WhaleGenie.Views;

/// <summary>气泡为什么收起。每种离场入口各有一个原因，关闭后的状态可以复核。</summary>
internal enum HintBubbleCloseReason
{
    PointerLeft,
    Toggled,
    Escape,
    OutsidePress,
    AnotherOpened,
    OwnerHidden,
    Detached,
    WindowHidden,
    TextCleared,
}
