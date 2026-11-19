namespace WhaleGenie.Views;

/// <summary>
/// 行内说明气泡的五个状态。悬停等待与关闭宽限各是一个状态，不再靠一个布尔字段同时表示
/// "正在等打开"和"正在等关闭"。
/// </summary>
internal enum HintBubbleState
{
    Closed,
    HoverPending,
    Open,
    Closing,
    Pinned,
}
