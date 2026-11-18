using System;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// 气泡等待的假时钟：登记一次任务，按推进的时间减少到期时间；到点才执行，执行后清空。
/// 无头会话不能真等 350 毫秒，也不该把"能编译"当成通过。
/// </summary>
internal sealed class ManualHintDelay : IHintDelayScheduler
{
    private Action? _pending;
    private TimeSpan _due;

    public void Schedule(TimeSpan delay, Action action)
    {
        _pending = action;
        _due = delay;
    }

    public void CancelAll()
    {
        _pending = null;
        _due = default;
    }

    public void Advance(TimeSpan by)
    {
        if (_pending is not { } action)
        {
            return;
        }

        _due -= by;
        if (_due > TimeSpan.Zero)
        {
            return;
        }

        _pending = null;
        action();
    }
}
