using System;
using Avalonia.Threading;

namespace WhaleGenie.Views;

/// <summary>
/// 一次可取消的延迟。气泡的 350 毫秒等待与 200 毫秒宽限各登记一次，状态转换先取消旧任务，
/// 于是同一个状态最多只有一个待执行任务；无头检查用假时钟推进它。
/// </summary>
internal interface IHintDelayScheduler
{
    void Schedule(TimeSpan delay, Action action);

    void CancelAll();
}

/// <summary>真实窗口里的延迟：一次只留一个 <see cref="DispatcherTimer"/>。</summary>
internal sealed class DispatcherHintDelay : IHintDelayScheduler
{
    private DispatcherTimer? _timer;

    public void Schedule(TimeSpan delay, Action action)
    {
        CancelAll();

        var timer = new DispatcherTimer { Interval = delay };
        timer.Tick += (_, _) =>
        {
            CancelAll();
            action();
        };
        _timer = timer;
        timer.Start();
    }

    public void CancelAll()
    {
        if (_timer is not { } timer)
        {
            return;
        }

        timer.Stop();
        _timer = null;
    }
}
