namespace WhaleGenie.ViewModels;

/// <summary>
/// 一次运行"处在什么状态"。**文字由本地化给，颜色由这个种类给**——
/// 两者分开，是因为状态要先被表达出来，颜色才有东西可表达。
/// </summary>
public enum RunStatusKind
{
    /// <summary>还没开始，或还没读到第一条结果。</summary>
    Ready,

    /// <summary>正在跑。</summary>
    Running,

    /// <summary>跑完了。</summary>
    Done,

    /// <summary>因为出错停下了。</summary>
    Failed,

    /// <summary>暂停着。</summary>
    Paused,
}
