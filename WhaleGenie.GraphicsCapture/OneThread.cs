using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace WhaleGenie.GraphicsCapture;

/// <summary>
/// One thread with a queue, for work that must not run side by side and must not run on the thread
/// that asked for it: the graphics work of reading the screen, which is called both from the
/// engine's own thread and from the interface's. <c>Run</c> waits, the way a device the engine
/// calls into does.
/// </summary>
internal sealed class OneThread : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _worker;

    public OneThread(string name)
    {
        _worker = new Thread(Pump) { IsBackground = true, Name = name };
        _worker.Start();
    }

    public T Run<T>(Func<T> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(() =>
        {
            try
            {
                done.SetResult(work());
            }
            catch (Exception error)
            {
                done.SetException(error);
            }
        });

        return done.Task.GetAwaiter().GetResult();
    }

    private void Pump()
    {
        foreach (var work in _queue.GetConsumingEnumerable())
        {
            work();
        }
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _worker.Join(TimeSpan.FromSeconds(5));
        _queue.Dispose();
    }
}
